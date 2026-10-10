import { initializeEditor, TiptapHelper, exportAsPlainText } from '../prose-mirror-bundle.js';

var _page;

// Initializes the prosemirror editor
export function initialize(dotnetPage, editorContainer, shoppingModeActive) {
    _page = dotnetPage;
    _page.editor = initializeEditor(editorContainer);

    _page.editor.on('selectionUpdate', onSelectionUpdate);
    _page.editor.on('update', onUpdate);

    document.addEventListener('custom-link-clicked', function () {
        _page.invokeMethodAsync('OpenLinkDialog');
    });
}

// Finalizes the prosemirror editor
export function finalize() {
    if (_page.editor) {
        _page.editor.off('selectionUpdate', onSelectionUpdate);
        _page.editor.off('update', onUpdate);
    }
    _page.editor = null;
    _page = null;
}

function IsInitialized() {
    return _page && _page.editor;
}

// By setting the content after loading the page, we can avoid that the content has to be
// declared pre rendered as javascript and therefore would occupy memory twice.
export function setNoteContent(text) {
    if (!IsInitialized()) return;

    try {
        _page.editor.chain().setMeta('addToHistory', false).setContent(text).run();
    }
    catch (ex) {
        _page.editor.setEditable(false);
    }
}

export function getAsPlainText() {
    return exportAsPlainText(_page.editor);
}

export function sessionSaveScrollTop(sessionId, editorContainer) {
    if (!IsInitialized()) return;
    TiptapHelper.sessionSaveScrollTop(sessionId, editorContainer);
}

export function sessionLoadScrollTop(sessionId, editorContainer) {
    if (!IsInitialized()) return;
    TiptapHelper.sessionLoadScrollTop(sessionId, editorContainer);
}

export function setEditable(editable) {
    if (!IsInitialized()) return;

    _page.editor.setEditable(editable);
}

export function toggleFormatAndRefresh(formatName, formatParameter) {
    if (!IsInitialized()) return;

    TiptapHelper.toggleFormat(_page.editor, formatName, formatParameter);
    onActiveFormatStateChanged();
}

export function callInsertHorizontalRule() {
    if (!IsInitialized()) return;

    TiptapHelper.insertHorizontalRule(_page.editor);
}

const MAX_IMAGE_EDGE = 1600;

// Inserts a picture, which was picked by the user, at the current position. The picture is
// downscaled in the webview to keep the note (and therefore the synchronized repository) small.
export async function insertImageFromBase64(base64) {
    if (!IsInitialized() || !base64) return;

    const bytes = base64ToBytes(base64);
    const mime = detectImageMime(bytes);
    const originalDataUrl = 'data:' + mime + ';base64,' + base64;

    try {
        const blob = new Blob([bytes], { type: mime });
        const { image, url } = await loadImageElement(blob);
        try {
            TiptapHelper.insertImage(_page.editor, downscaleImage(image, mime));
        } finally {
            URL.revokeObjectURL(url);
        }
    }
    catch (ex) {
        // Never lose the picked picture, insert it unmodified as a fallback.
        TiptapHelper.insertImage(_page.editor, originalDataUrl);
    }
}

function base64ToBytes(base64) {
    const binary = atob(base64);
    const bytes = new Uint8Array(binary.length);
    for (let i = 0; i < binary.length; i++)
        bytes[i] = binary.charCodeAt(i);
    return bytes;
}

function detectImageMime(bytes) {
    if (bytes.length >= 8 && bytes[0] === 0x89 && bytes[1] === 0x50 && bytes[2] === 0x4E && bytes[3] === 0x47)
        return 'image/png';
    if (bytes.length >= 3 && bytes[0] === 0xFF && bytes[1] === 0xD8 && bytes[2] === 0xFF)
        return 'image/jpeg';
    if (bytes.length >= 6 && bytes[0] === 0x47 && bytes[1] === 0x49 && bytes[2] === 0x46)
        return 'image/gif';
    if (bytes.length >= 12 && bytes[8] === 0x57 && bytes[9] === 0x45 && bytes[10] === 0x42 && bytes[11] === 0x50)
        return 'image/webp';
    if (bytes.length >= 2 && bytes[0] === 0x42 && bytes[1] === 0x4D)
        return 'image/bmp';
    return 'image/png';
}

function loadImageElement(blob) {
    return new Promise((resolve, reject) => {
        const url = URL.createObjectURL(blob);
        const image = new Image();
        image.onload = () => resolve({ image: image, url: url });
        image.onerror = (error) => {
            URL.revokeObjectURL(url);
            reject(error);
        };
        image.src = url;
    });
}

function downscaleImage(image, mime) {
    let width = image.naturalWidth || image.width;
    let height = image.naturalHeight || image.height;
    const scale = Math.min(1, MAX_IMAGE_EDGE / Math.max(width, height));
    if (scale < 1) {
        width = Math.max(1, Math.round(width * scale));
        height = Math.max(1, Math.round(height * scale));
    }

    const canvas = document.createElement('canvas');
    canvas.width = width;
    canvas.height = height;
    const context = canvas.getContext('2d');

    // Keep lossless formats (which can have transparency) as PNG, recompress pictures as JPEG.
    const keepPng = (mime === 'image/png' || mime === 'image/gif');
    if (!keepPng) {
        context.fillStyle = '#ffffff';
        context.fillRect(0, 0, width, height);
    }
    context.drawImage(image, 0, 0, width, height);

    return keepPng ? canvas.toDataURL('image/png') : canvas.toDataURL('image/jpeg', 0.85);
}

function onActiveFormatStateChanged() {
    if (!IsInitialized()) return;

    var states = [
        TiptapHelper.isFormatActive(_page.editor, 'heading', { level: 1 }),
        TiptapHelper.isFormatActive(_page.editor, 'heading', { level: 2 }),
        TiptapHelper.isFormatActive(_page.editor, 'heading', { level: 3 }),
        TiptapHelper.isFormatActive(_page.editor, 'bold'),
        TiptapHelper.isFormatActive(_page.editor, 'italic'),
        TiptapHelper.isFormatActive(_page.editor, 'underline'),
        TiptapHelper.isFormatActive(_page.editor, 'strike'),
        TiptapHelper.isFormatActive(_page.editor, 'codeblock'),
        TiptapHelper.isFormatActive(_page.editor, 'blockquote'),
        TiptapHelper.isFormatActive(_page.editor, 'bulletlist'),
        TiptapHelper.isFormatActive(_page.editor, 'orderedlist')
    ];
    _page.invokeMethodAsync('RefreshActiveFormatState', states);
}

function onSelectionUpdate(params) {
    onActiveFormatStateChanged();
}

function onUpdate(params) {
    onNoteContentChanged(params.editor);
}

function onNoteContentChanged(editor) {
    if (!IsInitialized()) return;

    var noteContent = editor.getHTML();
    _page.invokeMethodAsync('SetNoteContent', noteContent);
}

export function undo() {
    if (!IsInitialized()) return;

    _page.editor.commands.undo();
}

export function redo() {
    if (!IsInitialized()) return;

    _page.editor.commands.redo();
}

export function search(searchPattern) {
    if (!IsInitialized()) return;

    TiptapHelper.searchAndHighlight(_page.editor, searchPattern);
    TiptapHelper.selectNextWhileTyping(_page.editor);
}

export function findNext() {
    if (!IsInitialized()) return;

    TiptapHelper.selectNext(_page.editor);
}

export function findPrevious() {
    if (!IsInitialized()) return;

    TiptapHelper.selectPrevious(_page.editor);
}

export function prepareLinkDialog() {
    if (!IsInitialized()) return;

    _page.editor.chain().focus().run();
    var selectedUrl = _page.editor.getAttributes('link').href;
    if (selectedUrl) {
        _page.editor.commands.extendMarkRange('link');
        var text = TiptapHelper.selectWordAtCurrentPosition(_page.editor);
        return [
            selectedUrl,
            text,
        ];
    }
    else {
        var text = TiptapHelper.selectWordAtCurrentPosition(_page.editor);
        return [
            '',
            text,
        ];
    }
}

export function linkDialogOkPressed(oldLinkUrl, newLinkUrl, oldLinkTitle, newLinkTitle) {
    if (!IsInitialized()) return;

    var newLinkIsEmpty = !newLinkUrl;
    var urlChanged = newLinkUrl != oldLinkUrl;
    var titleChanged = newLinkTitle != oldLinkTitle;
    if (!urlChanged && !titleChanged)
        return;

    var commandChain = _page.editor.chain().focus().extendMarkRange('link');

    if (newLinkIsEmpty) {
        commandChain = commandChain.unsetLink();
    }
    else if (titleChanged) {
        var selection = _page.editor.view.state.selection;
        commandChain = commandChain.command(({ tr }) => {
            tr.insertText(newLinkTitle);
            return true;
        });
        commandChain = commandChain.setTextSelection({ from: selection.$from.pos, to: selection.$from.pos + newLinkTitle.length });
        commandChain = commandChain.setLink({ href: newLinkUrl });
    }
    else if (urlChanged) {
        commandChain = commandChain.setLink({ href: newLinkUrl });
    }
    commandChain.run();
}
