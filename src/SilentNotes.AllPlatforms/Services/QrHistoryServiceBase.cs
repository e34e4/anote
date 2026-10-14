// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using SilentNotes.Models;
using SilentNotes.Workers;

namespace SilentNotes.Services
{
    /// <summary>
    /// Abstract base implementation of the <see cref="IQrHistoryService"/> interface. It persists
    /// the history of scanned QR codes as a small XML file on the local device.
    /// </summary>
    public abstract class QrHistoryServiceBase : IQrHistoryService
    {
        /// <summary>Gets the injected dependency to an xml file service.</summary>
        protected readonly IXmlFileService _xmlFileService;
        private QrHistoryModel _model;

        /// <summary>
        /// Initializes a new instance of the <see cref="QrHistoryServiceBase"/> class.
        /// </summary>
        /// <param name="xmlFileService">The xml file service used to store the history.</param>
        public QrHistoryServiceBase(IXmlFileService xmlFileService)
        {
            _xmlFileService = xmlFileService;
        }

        /// <summary>
        /// Sub classes should override this method to define the directory where the QR history
        /// is stored.
        /// </summary>
        /// <returns>The full directory path for storing the history.</returns>
        protected abstract string GetLocation();

        private string FilePath
        {
            get { return Path.Combine(GetLocation(), QrHistoryModel.FileName); }
        }

        private QrHistoryModel Model
        {
            get { return _model ?? (_model = LoadHistoryOrDefault()); }
        }

        /// <inheritdoc/>
        public QrHistoryModel LoadHistoryOrDefault()
        {
            try
            {
                if (_xmlFileService.TryLoad(FilePath, out XDocument xml))
                    return XmlUtils.DeserializeFromXmlDocument<QrHistoryModel>(xml);
            }
            catch (Exception)
            {
            }
            return new QrHistoryModel();
        }

        /// <inheritdoc/>
        public QrItemModel AddText(string text, int maxItems)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;
            text = text.Trim();

            QrItemModel item = Model.Items.FirstOrDefault(candidate => string.Equals(candidate.Text, text, StringComparison.Ordinal));
            if (item != null)
            {
                // An identical item is moved to the top instead of being duplicated.
                Model.Items.Remove(item);
                item.CreatedAt = DateTime.Now;
            }
            else
            {
                item = new QrItemModel { Id = Guid.NewGuid(), Text = text };
                item.CreatedAt = DateTime.Now;
            }
            Model.Items.Insert(0, item);

            if (maxItems > 0)
            {
                while (Model.Items.Count > maxItems)
                    Model.Items.RemoveAt(Model.Items.Count - 1);
            }

            TrySave();
            return item;
        }

        /// <inheritdoc/>
        public void Delete(Guid id)
        {
            QrItemModel item = Model.Items.FirstOrDefault(candidate => candidate.Id == id);
            if (item != null)
            {
                Model.Items.Remove(item);
                TrySave();
            }
        }

        /// <inheritdoc/>
        public void ClearAll()
        {
            Model.Items.Clear();
            TrySave();
        }

        private bool TrySave()
        {
            try
            {
                Directory.CreateDirectory(GetLocation());
                return _xmlFileService.TrySerializeAndSave(FilePath, Model);
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
