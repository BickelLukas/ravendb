﻿using System;
using System.Collections.Generic;
using System.IO;
using Raven.Server.Documents;
using Raven.Server.Documents.TimeSeries;
using Sparrow.Json;
using Sparrow.Server;
using Voron;

namespace Raven.Server.Smuggler.Documents
{
    public class DocumentItem
    {
        public static class ExportDocumentType
        {
            public const string Key = "@export-type";

            public const string Document = nameof(Document);
            public const string Attachment = nameof(Attachment);
        }

        public Document Document;
        public List<AttachmentStream> Attachments;
        public Tombstone Tombstone;
        public DocumentConflict Conflict;

        public struct AttachmentStream : IDisposable
        {
            public Slice Base64Hash;
            public ByteStringContext.ExternalScope Base64HashDispose;

            public Slice Tag;
            public ByteStringContext.ExternalScope TagDispose;

            public Stream Stream;

            public BlittableJsonReaderObject Data;

            public void Dispose()
            {
                Base64HashDispose.Dispose();
                TagDispose.Dispose();
                Stream.Dispose();
                Data.Dispose();
            }
        }
    }

    public class CounterItem
    {
        public string DocId;
        public string ChangeVector;

        public struct Legacy
        {
            public string Name;
            public long Value;
        }

        public struct Batch
        {
            public BlittableJsonReaderObject Values;
        }
    }


    public class TimeSeriesItem
    {
        public string DocId; 

        public string Name;

        public string ChangeVector;

        public TimeSeriesValuesSegment Segment;

        public int SegmentSize;

        public string Collection;

        public DateTime Baseline;

    }
}
