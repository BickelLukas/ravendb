﻿using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Raven.Client.Documents.Changes;
using Raven.Client.Documents.Operations;
using Raven.Client.Util;
using Sparrow.Collections;
using Sparrow.Json;
using Sparrow.Json.Parsing;
using Sparrow.Threading;

namespace Raven.Server.Documents
{

    public class ChangesClientConnection : IDisposable
    {
        private static long _counter;

        private readonly WebSocket _webSocket;
        private readonly DocumentDatabase _documentDatabase;
        private readonly AsyncQueue<ChangeValue> _sendQueue = new AsyncQueue<ChangeValue>();

        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly CancellationToken _disposeToken;

        private readonly DateTime _startedAt;

        private readonly ConcurrentSet<string> _matchingIndexes =
            new ConcurrentSet<string>(StringComparer.OrdinalIgnoreCase);

        private readonly ConcurrentSet<string> _matchingDocuments =
            new ConcurrentSet<string>(StringComparer.OrdinalIgnoreCase);

        private readonly ConcurrentSet<string> _matchingDocumentPrefixes =
            new ConcurrentSet<string>(StringComparer.OrdinalIgnoreCase);

        private readonly ConcurrentSet<string> _matchingDocumentsInCollection =
            new ConcurrentSet<string>(StringComparer.OrdinalIgnoreCase);

        private readonly ConcurrentSet<string> _matchingDocumentsOfType =
            new ConcurrentSet<string>(StringComparer.OrdinalIgnoreCase);

        private readonly ConcurrentSet<long> _matchingOperations =
          new ConcurrentSet<long>();

        private int _watchAllDocuments;
        private int _watchAllOperations;
        private int _watchAllIndexes;

        public class ChangeValue
        {
            public DynamicJsonValue ValueToSend;
            public bool AllowSkip;
        }

        public ChangesClientConnection(WebSocket webSocket, DocumentDatabase documentDatabase)
        {
            _webSocket = webSocket;
            _documentDatabase = documentDatabase;
            _startedAt = SystemTime.UtcNow;
            _disposeToken = _cts.Token;
        }

        public long Id = Interlocked.Increment(ref _counter);

        public TimeSpan Age => SystemTime.UtcNow - _startedAt;

        public void WatchDocument(string docId)
        {
            _matchingDocuments.TryAdd(docId);
        }

        public void UnwatchDocument(string name)
        {
            _matchingDocuments.TryRemove(name);
        }

        public void WatchAllDocuments()
        {
            Interlocked.Increment(ref _watchAllDocuments);
        }

        public void UnwatchAllDocuments()
        {
            Interlocked.Decrement(ref _watchAllDocuments);
        }

        public void WatchDocumentPrefix(string name)
        {
            _matchingDocumentPrefixes.TryAdd(name);
        }

        public void UnwatchDocumentPrefix(string name)
        {
            _matchingDocumentPrefixes.TryRemove(name);
        }

        public void WatchDocumentInCollection(string name)
        {
            _matchingDocumentsInCollection.TryAdd(name);
        }

        public void UnwatchDocumentInCollection(string name)
        {
            _matchingDocumentsInCollection.TryRemove(name);
        }

        public void WatchDocumentOfType(string name)
        {
            _matchingDocumentsOfType.TryAdd(name);
        }

        public void UnwatchDocumentOfType(string name)
        {
            _matchingDocumentsOfType.TryRemove(name);
        }

        public void WatchAllIndexes()
        {
            Interlocked.Increment(ref _watchAllIndexes);
        }

        public void UnwatchAllIndexes()
        {
            Interlocked.Decrement(ref _watchAllIndexes);
        }

        public void WatchIndex(string name)
        {
            _matchingIndexes.TryAdd(name);
        }

        public void UnwatchIndex(string name)
        {
            _matchingIndexes.TryRemove(name);
        }

        private static bool HasItemStartingWith(ConcurrentSet<string> set, string value)
        {
            if (set.Count == 0)
                return false;
            foreach (string item in set)
            {
                if (value.StartsWith(item, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static bool HasItemEqualsTo(ConcurrentSet<string> set, string value)
        {
            if (set.Count == 0)
                return false;
            foreach (string item in set)
            {
                if (value.Equals(item, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        public void SendDocumentChanges(DocumentChange change)
        {
            // this is a precaution, in order to overcome an observed race condition between change client disconnection and raising changes
            if (IsDisposed)
                return;
            if (_watchAllDocuments > 0)
            {
                Send(change);
                return;
            }

            if (change.Id != null && _matchingDocuments.Contains(change.Id))
            {
                Send(change);
                return;
            }

            var hasPrefix = change.Id != null && HasItemStartingWith(_matchingDocumentPrefixes, change.Id);
            if (hasPrefix)
            {
                Send(change);
                return;
            }

            var hasCollection = change.CollectionName != null && HasItemEqualsTo(_matchingDocumentsInCollection, change.CollectionName);
            if (hasCollection)
            {
                Send(change);
                return;
            }

            var hasType = change.TypeName != null && HasItemEqualsTo(_matchingDocumentsOfType, change.TypeName);
            if (hasType)
            {
                Send(change);
                return;
            }

            if (change.Id == null && change.CollectionName == null && change.TypeName == null)
            {
                Send(change);
            }
        }

        public void SendIndexChanges(IndexChange change)
        {
            if (_watchAllIndexes > 0)
            {
                Send(change);
                return;
            }

            if (change.Name != null && _matchingIndexes.Contains(change.Name))
            {
                Send(change);
            }
        }

        private void Send(DocumentChange change)
        {
            var value = new DynamicJsonValue
            {
                ["Type"] = nameof(DocumentChange),
                ["Value"] = change.ToJson()
            };

            if (_disposeToken.IsCancellationRequested == false)
                _sendQueue.Enqueue(new ChangeValue
                {
                    ValueToSend = value,
                    AllowSkip = true
                });
        }

        private void Send(IndexChange change)
        {
            var value = new DynamicJsonValue
            {
                ["Type"] = nameof(IndexChange),
                ["Value"] = change.ToJson()
            };

            if (_disposeToken.IsCancellationRequested == false)
                _sendQueue.Enqueue(new ChangeValue
                {
                    ValueToSend = value,
                    AllowSkip = change.Type == IndexChangeTypes.BatchCompleted //TODO: make sure it makes sense
                });
        }

        public void WatchOperation(long operationId)
        {
            _matchingOperations.TryAdd(operationId);
        }

        public void UnwatchOperation(long operationId)
        {
            _matchingOperations.TryRemove(operationId);
        }

        public void WatchAllOperations()
        {
            Interlocked.Increment(ref _watchAllOperations);
        }

        public void UnwatchAllOperations()
        {
            Interlocked.Decrement(ref _watchAllOperations);
        }

        public void SendOperationStatusChangeNotification(OperationStatusChange change)
        {
            if (_watchAllOperations > 0)
            {
                Send(change);
                return;
            }

            if (_matchingOperations.Contains(change.OperationId))
            {
                Send(change);
            }
        }

        private void Send(OperationStatusChange change)
        {
            var value = new DynamicJsonValue
            {
                ["Type"] = nameof(OperationStatusChange),
                ["Value"] = change.ToJson()
            };

            if (_disposeToken.IsCancellationRequested == false)
                _sendQueue.Enqueue(new ChangeValue
                {
                    ValueToSend = value,
                    AllowSkip = false
                });
        }

        public async Task StartSendingNotifications(bool throttleConnection)
        {
            using (_documentDatabase.DocumentsStorage.ContextPool.AllocateOperationContext(out JsonOperationContext context))
            {
                using (var ms = new MemoryStream())
                {
                    var sp = Stopwatch.StartNew();
                    while (true)
                    {
                        if (_disposeToken.IsCancellationRequested)
                            break;

                        ms.SetLength(0);
                        using (var writer = new BlittableJsonTextWriter(context, ms))
                        {
                            sp.Restart();

                            var first = true;
                            writer.WriteStartArray();

                            do
                            {
                                var value = await GetNextMessage(throttleConnection);
                                if (value == null || _disposeToken.IsCancellationRequested)
                                    break;

                                if (first == false)
                                    writer.WriteComma();

                                first = false;
                                context.Write(writer, value);

                                writer.Flush();

                                if (ms.Length > 16 * 1024)
                                    break;
                            } while (_sendQueue.Count > 0 && sp.Elapsed < TimeSpan.FromSeconds(5));

                            writer.WriteEndArray();
                        }

                        if (_disposeToken.IsCancellationRequested)
                            break;

                        ms.TryGetBuffer(out ArraySegment<byte> bytes);
                        await _webSocket.SendAsync(bytes, WebSocketMessageType.Text, true, _disposeToken);
                    }
                }
            }
        }

        private DynamicJsonValue _skippedMessage;
        private DateTime _lastSendMessage;

        private async Task<DynamicJsonValue> GetNextMessage(bool throttleConnection)
        {
            while (true)
            {
                var nextMessage = await _sendQueue.TryDequeueAsync(TimeSpan.FromSeconds(5));
                if (nextMessage.Item1 == false)
                {
                    var dynamicJsonValue = _skippedMessage;
                    _skippedMessage = null;
                    return dynamicJsonValue;
                }
                var msg = nextMessage.Item2;
                if (throttleConnection && msg.AllowSkip)
                {
                    if (DateTime.UtcNow - _lastSendMessage < TimeSpan.FromSeconds(5))
                    {
                        _skippedMessage = msg.ValueToSend;
                        continue;
                    }
                }
                _skippedMessage = null;
                _lastSendMessage = DateTime.UtcNow;
                return msg.ValueToSend;
            }
        }

        private SingleUseFlag _isDisposed = new SingleUseFlag();
        public bool IsDisposed => _isDisposed.IsRaised();

        public void Dispose()
        {
            _isDisposed.Raise();
            _cts.Cancel();
            _sendQueue.Enqueue(new ChangeValue
            {
                AllowSkip = false,
                ValueToSend = null
            });
            _cts.Dispose();
        }

        public void Confirm(int commandId)
        {
            _sendQueue.Enqueue(new ChangeValue
            {
                ValueToSend = new DynamicJsonValue
                {
                    ["CommandId"] = commandId,
                    ["Type"] = "Confirm"
                },
                AllowSkip = false
            });
        }

        public void HandleCommand(string command, string commandParameter)
        {
            long.TryParse(commandParameter, out long commandParameterAsLong);

            if (Match(command, "watch-index"))
            {
                WatchIndex(commandParameter);
            }
            else if (Match(command, "unwatch-index"))
            {
                UnwatchIndex(commandParameter);
            }
            else if (Match(command, "watch-indexes"))
            {
                WatchAllIndexes();
            }
            else if (Match(command, "unwatch-indexes"))
            {
                UnwatchAllIndexes();
            }
            else if (Match(command, "watch-doc"))
            {
                WatchDocument(commandParameter);
            }
            else if (Match(command, "unwatch-doc"))
            {
                UnwatchDocument(commandParameter);
            }
            else if (Match(command, "watch-docs"))
            {
                WatchAllDocuments();
            }
            else if (Match(command, "unwatch-docs"))
            {
                UnwatchAllDocuments();
            }
            else if (Match(command, "watch-prefix"))
            {
                WatchDocumentPrefix(commandParameter);
            }
            else if (Equals(command, "unwatch-prefix"))
            {
                UnwatchDocumentPrefix(commandParameter);
            }
            else if (Match(command, "watch-collection"))
            {
                WatchDocumentInCollection(commandParameter);
            }
            else if (Equals(command, "unwatch-collection"))
            {
                UnwatchDocumentInCollection(commandParameter);
            }
            else if (Match(command, "watch-type"))
            {
                WatchDocumentOfType(commandParameter);
            }
            else if (Equals(command, "unwatch-type"))
            {
                UnwatchDocumentOfType(commandParameter);
            }
            else if (Equals(command, "watch-operation"))
            {
                WatchOperation(commandParameterAsLong);
            }
            else if (Equals(command, "unwatch-operation"))
            {
                UnwatchOperation(commandParameterAsLong);
            }
            else if (Equals(command, "watch-operations"))
            {
                WatchAllOperations();
            }
            else if (Equals(command, "unwatch-operations"))
            {
                UnwatchAllOperations();
            }
            else
            {
                throw new ArgumentOutOfRangeException(nameof(command), "Command argument is not valid");
            }
        }

        protected static bool Match(string x, string y)
        {
            return string.Equals(x, y, StringComparison.OrdinalIgnoreCase);
        }

        public DynamicJsonValue GetDebugInfo()
        {
            return new DynamicJsonValue
            {
                ["Id"] = Id,
                ["State"] = _webSocket.State.ToString(),
                ["CloseStatus"] = _webSocket.CloseStatus,
                ["CloseStatusDescription"] = _webSocket.CloseStatusDescription,
                ["SubProtocol"] = _webSocket.SubProtocol,
                ["Age"] = Age,
                ["WatchAllDocuments"] = _watchAllDocuments > 0,
                ["WatchAllIndexes"] = false,
                /*["WatchConfig"] = _watchConfig > 0,
                ["WatchConflicts"] = _watchConflicts > 0,
                ["WatchSync"] = _watchSync > 0,*/
                ["WatchDocumentPrefixes"] = _matchingDocumentPrefixes.ToArray(),
                ["WatchDocumentsInCollection"] = _matchingDocumentsInCollection.ToArray(),
                ["WatchIndexes"] = _matchingIndexes.ToArray(),
                ["WatchDocuments"] = _matchingDocuments.ToArray()
            };
        }
    }
}
