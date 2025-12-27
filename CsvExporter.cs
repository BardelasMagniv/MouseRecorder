using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace MouseRecorder
{
    internal class CsvExporter : IDisposable
    {
        private readonly string _tmpPath;
        private readonly string _finalPath;
        private StreamWriter? _writer;
        private long _eventCounter = 0;
        private readonly object _lock = new object();

        public CsvExporter(string tmpPath)
        {
            _tmpPath = tmpPath;
            _finalPath = Path.ChangeExtension(_tmpPath, ".csv");
            _writer = new StreamWriter(new FileStream(_tmpPath, FileMode.Create, FileAccess.Write, FileShare.Read), Encoding.UTF8);
            _writer.AutoFlush = true;
        }


        public void WriteSessionMetadata(SessionMetadata meta)
        {
            lock (_lock)
            {
                _writer?.WriteLine($"#SESSION_ID: {meta.SessionId}");
                _writer?.WriteLine($"#SESSION_START_UTC: {meta.SessionStartUtc:O}");
                _writer?.WriteLine($"#RECORDING_MODE: {meta.RecordingMode}");
                _writer?.WriteLine($"#IS_PASSIVE: {meta.IsPassive}");
                _writer?.WriteLine($"#SCREEN_RESOLUTION: {meta.ScreenWidth}x{meta.ScreenHeight}");
                _writer?.WriteLine($"#WINDOW_POS: {meta.WindowLeft},{meta.WindowTop}");
                _writer?.WriteLine($"#WINDOW_SIZE: {meta.WindowWidth}x{meta.WindowHeight}");
                _writer?.WriteLine($"#DPI: {meta.DpiX}x{meta.DpiY}");
                _writer?.WriteLine("#EVENTS");
                _writer?.WriteLine("event_index,event_type,trial_id,button,button_state,x,y,norm_x,norm_y,raw_dx,raw_dy,wheel_delta,ts_monotonic_us,ts_utc_iso,qpc_ticks,screen_w,screen_h,window_left,window_top,window_w,window_h,coordinate_space,device_name,spawn_w,spawn_h,spawn_margin,comment");
            }
        }

        public void AppendEvents(IEnumerable<MouseEvent> events)
        {
            lock (_lock)
            {
                foreach (var e in events)
                {
                    _eventCounter++;
                    e.EventIndex = _eventCounter;
                    _writer?.WriteLine(FormatEvent(e));
                }
            }
        }

        private string FormatEvent(MouseEvent e)
        {
            string Safe(object? o)
            {
                if (o == null) return "";
                var s = Convert.ToString(o, CultureInfo.InvariantCulture) ?? "";
                if (s.Contains(",") || s.Contains("\"") || s.Contains("\n"))
                {
                    s = "\"" + s.Replace("\"", "\"\"") + "\"";
                }
                return s;
            }

            var parts = new List<string>
            {
                Safe(e.EventIndex),
                Safe(e.EventType),
                Safe(e.TrialId),
                Safe(e.Button),
                Safe(e.ButtonState),
                Safe(e.X),
                Safe(e.Y),
                Safe(e.NormX?.ToString("G6", CultureInfo.InvariantCulture)),
                Safe(e.NormY?.ToString("G6", CultureInfo.InvariantCulture)),
                Safe(e.RawDx),
                Safe(e.RawDy),
                Safe(e.WheelDelta),
                Safe(e.TsMonotonicUs),
                Safe(e.TsUtcIso),
                Safe(e.QpcTicks),
                Safe(e.ScreenW),
                Safe(e.ScreenH),
                Safe(e.WindowLeft),
                Safe(e.WindowTop),
                Safe(e.WindowW),
                Safe(e.WindowH),
                Safe(e.CoordinateSpace),
                Safe(e.DeviceName),
                Safe(e.SpawnW),
                Safe(e.SpawnH),
                Safe(e.SpawnMargin),
                Safe(e.Comment)
            };
            return string.Join(",", parts);
        }

        public void FinalizeAndClose()
        {
            lock (_lock)
            {
                _writer?.Flush();
                _writer?.Close();
                _writer = null;
                // Rename tmp to final path
                try
                {
                    if (File.Exists(_finalPath)) File.Delete(_finalPath);
                    File.Move(_tmpPath, _finalPath);
                }
                catch
                {
                    // ignore for prototype
                }
            }
        }

        public void Dispose()
        {
            FinalizeAndClose();
        }
    }
}
