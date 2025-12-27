using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MouseRecorder
{
    internal class MouseLogger : IDisposable
    {
        private readonly ConcurrentQueue<MouseEvent> _queue = new ConcurrentQueue<MouseEvent>();
        private readonly CsvExporter _exporter;
        private CancellationTokenSource? _cts;
        private Task? _worker;
        private readonly int _flushMs = 200;

        public MouseLogger(Guid sessionId, long sessionStartTicks, DateTimeOffset sessionStartUtc, string tmpPath)
        {
            _exporter = new CsvExporter(tmpPath);
        }

        public int QueueSize => _queue.Count;

        public void WriteSessionMetadata(SessionMetadata meta)
        {
            _exporter.WriteSessionMetadata(meta);
        }

        public void Start()
        {
            _cts = new CancellationTokenSource();
            _worker = Task.Run(async () =>
            {
                while (!_cts.IsCancellationRequested)
                {
                    DrainAndWriteBatch();
                    try { await Task.Delay(_flushMs, _cts.Token); } catch { break; }
                }
                DrainAndWriteBatch();
            }, _cts.Token);
        }

        public void EnqueueEvent(MouseEvent e)
        {
            _queue.Enqueue(e);
        }

        private void DrainAndWriteBatch()
        {
            List<MouseEvent> batch = new List<MouseEvent>();
            while (batch.Count < 4096 && _queue.TryDequeue(out var ev))
            {
                batch.Add(ev);
            }
            if (batch.Count > 0)
            {
                _exporter.AppendEvents(batch);
            }
        }

        public void StopAndClose()
        {
            if (_cts != null)
            {
                _cts.Cancel();
                try { _worker?.Wait(2000); } catch { }
            }
            // final drain
            DrainAndWriteBatch();
            _exporter.FinalizeAndClose();
        }

        public void Dispose()
        {
            StopAndClose();
        }
    }
}
