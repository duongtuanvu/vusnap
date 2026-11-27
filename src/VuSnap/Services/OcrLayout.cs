using System.Globalization;
using System.Text;

namespace VuSnap.Services;

public sealed record OcrWord(string Text,double X,double Y,double Width,double Height);

public static class OcrLayout
{
    private const int MaxWords = 20000, MaxColumns = 512, MaxOutput = 1_000_000;
    private sealed class Row
    {
        public List<OcrWord> Words { get; } = new();
        public double Center { get; set; }
        public double Height { get; set; }
    }

    public static string Format(IEnumerable<OcrWord> input)
    {
        var words = input.Take(MaxWords+1).Where(w => !string.IsNullOrWhiteSpace(w.Text) &&
            w.Text.Length <= 4096 && double.IsFinite(w.X) && double.IsFinite(w.Y) &&
            double.IsFinite(w.Width) && double.IsFinite(w.Height) && w.X >= 0 && w.Y >= 0 &&
            w.Width > 0 && w.Height > 0 && w.X <= 65536 && w.Y <= 65536 &&
            w.Width <= 65536 && w.Height <= 65536).ToArray();
        if(words.Length > MaxWords) throw new UtilityException("ERR_OCR_004");
        if(words.Length == 0) return "";
        double Median(IEnumerable<double> values) { var a=values.OrderBy(v=>v).ToArray(); return a[a.Length/2]; }
        double pitch=Math.Max(1,Median(words.Select(w=>w.Width/Math.Max(1,new StringInfo(w.Text).LengthInTextElements))));
        double height=Median(words.Select(w=>w.Height));
        double origin=words.Min(w=>w.X);
        var rows=new List<Row>();
        foreach(var w in words.OrderBy(w=>w.Y+w.Height/2).ThenBy(w=>w.X))
        {
            double center=w.Y+w.Height/2;
            var row=rows.LastOrDefault();
            if(row is null || Math.Abs(center-row.Center)>Math.Max(2,Math.Min(height,row.Height)*0.55))
            {
                row=new Row{Center=center,Height=w.Height}; rows.Add(row);
            }
            row.Center=(row.Center*row.Words.Count+center)/(row.Words.Count+1);
            row.Words.Add(w);
        }
        var output=new StringBuilder();
        double? previous=null;
        foreach(var row in rows)
        {
            if(previous is double py)
            {
                output.AppendLine();
                if(row.Center-py>height*2.4) output.AppendLine();
            }
            previous=row.Center;
            int col=0;
            foreach(var w in row.Words.OrderBy(w=>w.X))
            {
                string text=w.Text.Replace("\r"," ").Replace("\n"," ").Replace("\t"," ").Trim();
                if(text.Length==0) continue;
                int target=(int)Math.Clamp(Math.Round((w.X-origin)/pitch),0,MaxColumns);
                int spaces=Math.Max(col==0?0:1,target-col);
                output.Append(' ',spaces).Append(text);
                col+=spaces+new StringInfo(text).LengthInTextElements;
                if(output.Length>MaxOutput) throw new UtilityException("ERR_OCR_004");
            }
        }
        return output.ToString().TrimEnd();
    }

    public static string ParseTsv(string tsv)
    {
        var words=new List<OcrWord>();
        using var reader=new StringReader(tsv);
        string? line;
        while((line=reader.ReadLine()) is not null)
        {
            var p=line.Split('\t',12);
            if(p.Length!=12 || p[0]!="5" || string.IsNullOrWhiteSpace(p[11])) continue;
            if(!double.TryParse(p[6],NumberStyles.Integer,CultureInfo.InvariantCulture,out var x) ||
               !double.TryParse(p[7],NumberStyles.Integer,CultureInfo.InvariantCulture,out var y) ||
               !double.TryParse(p[8],NumberStyles.Integer,CultureInfo.InvariantCulture,out var w) ||
               !double.TryParse(p[9],NumberStyles.Integer,CultureInfo.InvariantCulture,out var h)) continue;
            if(words.Count>=MaxWords) throw new UtilityException("ERR_OCR_004");
            words.Add(new OcrWord(p[11],x,y,w,h));
        }
        return Format(words);
    }
}
