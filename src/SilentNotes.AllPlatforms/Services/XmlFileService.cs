// Copyright © 2018 Martin Stoeckli.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.IO;
using System.Text;
using System.Xml.Linq;
using SilentNotes.Workers;

namespace SilentNotes.Services
{
    /// <summary>
    /// Implementation of the <see cref="IXmlFileService"/> interface.
    /// </summary>
    public class XmlFileService : IXmlFileService
    {
        /// <inheritdoc/>
        public bool TryLoad(string filePath, out XDocument xml)
        {
            try
            {
                // An interrupted writing operation must be completed before reading, otherwise the
                // newest content stays in the temporary file, and its leftover ready-file would
                // block all following write operations permanently.
                TryCompletePendingWrite(filePath);
                xml = XDocument.Load(filePath);
                return true;
            }
            catch (Exception)
            {
                xml = null;
                return false;
            }
        }

        /// <inheritdoc/>
        public bool TrySerializeAndSave(string filePath, object serializeableObject)
        {
            try
            {
                CreateFileWriter().Write(
                    filePath,
                    (xmlStream) => {
                        XmlUtils.SerializeToXmlStream(serializeableObject, xmlStream, Encoding.UTF8);
                    });
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <inheritdoc/>
        public bool Exists(string filePath)
        {
            TryCompletePendingWrite(filePath);
            return File.Exists(filePath);
        }

        /// <summary>
        /// Completes an unfinished atomic writing operation, so a leftover ready-file cannot block
        /// the file any more. A failing recovery must not prevent reading the intact original file.
        /// </summary>
        /// <param name="filePath">The full path of the file to recover.</param>
        private static void TryCompletePendingWrite(string filePath)
        {
            try
            {
                CreateFileWriter().CompletePendingWrite(filePath);
            }
            catch (Exception)
            {
            }
        }

        private static AtomicFileWriter CreateFileWriter()
        {
            return new AtomicFileWriter { MinValidFileSize = "</silentnotes>".Length };
        }
    }
}