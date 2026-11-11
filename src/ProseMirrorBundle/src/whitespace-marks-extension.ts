import { Extension } from '@tiptap/core'
import { Plugin, PluginKey } from 'prosemirror-state'
import { Decoration, DecorationSet } from 'prosemirror-view'
import type { Node as ProsemirrorNode } from 'prosemirror-model'

/**
 * Shows whitespace glyphs (· for spaces, → for tabs).
 * End-of-line markers are intentionally not shown.
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

function buildWhitespaceDecorations(doc: ProsemirrorNode): DecorationSet {
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
    }
  })

  return DecorationSet.create(doc, decorations)
}
