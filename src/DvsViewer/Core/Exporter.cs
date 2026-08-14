using System.IO;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace DvsViewer.Core;


public static class Exporter
{
    public static void WriteGpsCsv(string path, IReadOnlyList<GpsFix> gps)
    {
        var rows = gps.Select(g => new[] { g.TimeUtc, F(g.Lat), F(g.Lng), F3(g.SpeedKmh), F1(g.Heading) });
        WriteCsv(path, rows, new[] { "time_utc", "lat", "lng", "speed_kmh", "heading" });
    }

    public static void WriteGpx(string path, IReadOnlyList<GpsFix> gps)
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
        sb.Append("<gpx version=\"1.1\" creator=\"DVSS Extractor\" xmlns=\"http://www.topografix.com/GPX/1/1\">\n");
        sb.Append("  <metadata><name>DVSS GPS Track</name></metadata>\n");
        sb.Append("  <trk><name>Vehicle</name><trkseg>\n");
        foreach (var r in gps)
        {
            sb.Append($"    <trkpt lat=\"{F7(r.Lat)}\" lon=\"{F7(r.Lng)}\"><time>{r.TimeUtc}</time><speed>{F3(r.SpeedKmh)}</speed><course>{F1(r.Heading)}</course></trkpt>\n");
        }
        sb.Append("  </trkseg></trk>\n</gpx>\n");
        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    public static void WriteKml(string path, IReadOnlyList<GpsFix> gps)
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
        sb.Append("<kml xmlns=\"http://www.opengis.net/kml/2.2\"><Document><name>DVSS GPS</name>\n");
        sb.Append("  <Style id='acc'><IconStyle><scale>0.6</scale></IconStyle></Style>\n");
        foreach (var r in gps)
        {
            sb.Append($"  <Placemark><name>{F1(r.SpeedKmh)} km/h</name><styleUrl>#acc</styleUrl><Point><coordinates>{F7(r.Lng)},{F7(r.Lat)},0</coordinates></Point></Placemark>\n");
        }
        sb.Append("</Document></kml>\n");
        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    public static void WriteGeoJson(string path, IReadOnlyList<GpsFix> gps)
    {
        var coords = new StringBuilder();
        var props = new StringBuilder();
        foreach (var r in gps)
        {
            coords.Append($"[{F7(r.Lng)},{F7(r.Lat)}],");
            props.Append($"{{\"time\":\"{r.TimeUtc}\",\"speed_kmh\":{F3(r.SpeedKmh)},\"heading\":{F1(r.Heading)}}},");
        }
        if (coords.Length > 0) { coords.Length -= 1; props.Length -= 1; }

        var json = $"{{\"type\":\"FeatureCollection\",\"features\":[{{\"type\":\"Feature\",\"geometry\":{{\"type\":\"LineString\",\"coordinates\":[{coords}]}},\"properties\":{{\"name\":\"DVSS GPS track\"}}}}]}}";
        File.WriteAllText(path, json, Encoding.UTF8);
    }

    public static void WriteFramesCsv(string path, IReadOnlyList<VideoFrame> frames)
    {
        var rows = frames.Select((f, i) =>
        {
            double? lat = null, lng = null;
            if (f.Ll.Contains(','))
            {
                var parts = f.Ll.Split(',');
                if (parts.Length >= 2 && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var la)
                    && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var ln))
                {
                    lat = la; lng = ln;
                }
            }
            return new[]
            {
                i.ToString(), f.Channel, f.ChId.ToString(), f.Dt, f.TimeUtc,
                lat is null ? "" : F(lat.Value), lng is null ? "" : F(lng.Value),
                f.Ll, f.Alarms, f.Hash, f.Codec ?? "", f.Offset.ToString(), f.H264Length.ToString(),
            };
        });
        WriteCsv(path, rows, new[] { "index", "channel", "ch_id", "dt", "time_utc", "lat", "lng", "ll",
            "alarms", "hash", "codec", "offset", "size" });
    }

    public static void WriteFrameMetadata(string path, IReadOnlyList<VideoFrame> frames)
    {
        var sb = new StringBuilder();
        foreach (var f in frames)
        {
            sb.Append($"{f.Offset}|{f.Dt}|{f.Channel}|{f.Ll}|{f.Alarms}|{f.Hash}|{f.TimeUtc}\n");
        }
        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    public static void WriteReportJson(string path, object report)
    {
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        });
        File.WriteAllText(path, json, Encoding.UTF8);
    }

    public static void WriteCsv(string path, IEnumerable<IEnumerable<string>> rows, IReadOnlyList<string> header)
    {
        using var sw = new StreamWriter(path, false, new UTF8Encoding(false));
        sw.WriteLine(string.Join(",", header.Select(CsvField)));
        foreach (var row in rows)
            sw.WriteLine(string.Join(",", row.Select(CsvField)));
    }

    private static string CsvField(string v)
    {
        if (v.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0) return v;
        return "\"" + v.Replace("\"", "\"\"") + "\"";
    }

    private static string F(double v) => v.ToString("R", CultureInfo.InvariantCulture);
    private static string F7(double v) => v.ToString("F7", CultureInfo.InvariantCulture);
    private static string F3(double v) => v.ToString("F3", CultureInfo.InvariantCulture);
    private static string F1(double v) => v.ToString("F1", CultureInfo.InvariantCulture);
}
