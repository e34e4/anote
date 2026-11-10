import { Extension } from '@tiptap/core'
import { Plugin, PluginKey } from 'prosemirror-state'
import { Decoration, DecorationSet, Decoration } from 'prosemirror-view'
import type { Node as ProsemirrorNode } from 'prosemirror-model'

/**
 * Shows whitespace glyphs (· for spaces, → for tabs, ↵ for line ends)
 * in the TipTap/ProseMirror note editor, matching the reader text editor.
 */
export const WhitespaceMarks = Extension.create({
  name: 'whitespaceMarks',

  addProseMirrorPlugins() {
    return [
      new Plugin({
        key: new PluginKey('whitespaceMarks'),
        props: {
          decorations(state) {
            return buildWhitespaceDecorations(state.doc)
          },
        },
      }),
    ]
  },
})

function buildWhitespaceDecorations(doc: ProsemirrorNode): Decoration {
  const decorations: Decoration[] = []

  doc.descendants((node, pos) => {
    if (node.isText && node.text) {
      const text = node.text
      for (let i = 0; i < text.length; i++) {
        const ch = text.charAt(i)
        if (ch === ' ') {
          decorations.push(Decoration.inline(pos + i, pos + i + 1, { class: 'pm-space' }))
        } else if (ch === '\t') {
          decorations.push(Decoration.inline(pos + i, pos + i + 1, { class: 'pm-tab' }))
        }
      }
      return
    }

    if (node.type.name === 'hardBreak') {
      decorations.push(eolWidget(pos + 1))
      return
    }

    // End-of-block marker (paragraph, heading, list item textblock, etc.)
    if (node.isTextblock) {
      const endPos = pos + node.nodeSize - 1
      decorations.push(eolWidget(endPos))
    }
  })

  return DecorationSet.create(doc, decorations)
}

function eolWidget(pos: number): Decoration {
  return Decoration.widget(pos, () => {
    const span = document.createElement('span')
    span.className = 'pm-eol'
    span.textContent = '↵'
    span.setAttribute('contenteditable', 'false')
    return span
  }, { side: 1, key: 'eol-' + pos })
}
