// Adapted from Stackshot, MIT, Copyright (c) 2026 Rubén Martínez (@rubenitx).
// Source: https://github.com/rubenitx/stackshot/blob/57d2e2cd86e332ab309b88925293f43575082d60/src/Capture/ScrollCapture.cs
// Only the pure Stitcher is reused; no upstream UI, network, updater or input hooks.
#nullable disable
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using VuSnap.Models;

namespace VuSnap.Core
{
    // Stitches consecutive frames of a scrolling area. Each row is hashed; the shift is the offset most unique rows
    // agree on (voting), confirmed by comparing the whole overlap. Static rows at the top (header) and bottom (footer)
    // are excluded. When exact rows don't match (content resampled by a fractional scroll), row brightness profiles
    // decide instead. Floating elements that stay put while the page moves (a chat button) are found per cell and
    // painted over with the content that later shows from under them.
    public sealed class Stitcher
    {
        public enum Result { First, Same, Added, Lost, Full, Up }

        public const long MaximumPixels = 24_000_000;
        public const int MaximumDimension = 16384;
        public const long MaximumFramePixels = 8_000_000;
        public const int MaximumFrameDimension = 4096;
        private bool composed;
        private CancellationToken cancel;

        public static void ValidateFrame(int width, int height)
        {
            if (width < 40 || height < 40 || width > MaximumFrameDimension || height > MaximumFrameDimension || (long)width * height > MaximumFramePixels)
                throw new CaptureException("ERR_SCROLL_001");
        }

        const int UpShift = -2;           // BestShift: the content moved down (scrolled up)
        const int Bins = 32;              // brightness bands per row for the tolerant match
        const int Cell = 8;               // floating elements are tracked in cells of this size

        readonly int w, h, hashW, maxRows, bins, cw, ch;
        readonly List<int[]> rows = new List<int[]>();
        readonly List<int> src = new List<int>();     // per stitched row, the frame row it was copied from
        readonly HashSet<long> cleaned = new HashSet<long>(); // stitched row * cw + cell column, repainted from under a float
        readonly byte[] still;            // per cell: scrolls in a row it stayed put while the page moved
        int[] prev;
        ulong[] prevHash;
        bool[] prevFlat;
        float[] prevFeat, curFeat;
        int[] curFeatOf;
        volatile int height;

        public Stitcher(int width, int height, int ignoreRight, int maxRows)
        {
            ValidateFrame(width, height);
            if (maxRows < height || ignoreRight < 0 || ignoreRight >= width / 2) throw new CaptureException("ERR_SCROLL_001");
            w = width;
            h = height;
            hashW = Math.Max(1, width - Math.Max(0, ignoreRight));
            this.maxRows = Math.Min(Math.Min(MaximumDimension, maxRows), (int)(MaximumPixels / width));
            bins = Math.Min(Bins, hashW);
            cw = (w + Cell - 1) / Cell;
            ch = (h + Cell - 1) / Cell;
            still = new byte[cw * ch];
        }

        bool Floating(int cx, int cy) { return still[cy * cw + cx] >= 2; }

        public int Height { get { return height; } }
        public int Width { get { return w; } }
        public bool IsFull { get { return height >= maxRows; } }

        // Adds a frame (top-down pixels, w x h). Returns a buffer the caller can reuse: px if it was not kept, or the
        // previous frame if px became the new reference.
        public int[] Add(int[] px, out Result result, out int added, CancellationToken cancellation = default)
        {
            cancellation.ThrowIfCancellationRequested();
            if (composed || px is null || px.Length != checked(w * h) || ReferenceEquals(px, prev)) throw new CaptureException("ERR_SCROLL_004");
            cancel = cancellation;
            added = 0;
            // Profiles worked out by an earlier call may belong to whatever this reused buffer held then.
            curFeat = null;
            curFeatOf = null;
            ulong[] hs = new ulong[h];
            bool[] flat = new bool[h];
            HashRows(px, hs, flat);
            if (prev == null)
            {
                for (int y = 0; y < h; y++) { rows.Add(Row(px, y)); src.Add(y); }
                height = rows.Count;
                Keep(px, hs, flat);
                added = h;
                result = Result.First;
                return null;
            }

            // Rows unchanged in place: header at the top, footer at the bottom.
            int top = 0;
            while (top < h && hs[top] == prevHash[top]) top++;
            if (top == h) { result = Result.Same; return px; }
            int bot = 0;
            while (bot < h - top && hs[h - 1 - bot] == prevHash[h - 1 - bot]) bot++;

            int d = BestShift(hs, flat, top, h - bot);
            bool tolerant = false;
            if (d == UpShift) { result = h - top - bot < h / 6 ? Result.Same : Result.Up; return px; }
            if (d < 0)
            {
                // A small changed band (blinking caret, video) means no scroll.
                if (h - top - bot < h / 6) { result = Result.Same; return px; }
                d = Tolerant(px, flat, top, h - bot);
                if (d < 0) { result = Result.Lost; return px; }
                tolerant = true;
            }
            if (d == 0) { result = Result.Same; return px; }       // something animates, but nothing scrolled
            // A fraction of a row left over is carried to the next frame, once this one is kept: a page that moves 4.2
            // rows a frame gets a fifth row every fifth frame instead of slowly losing it.
            if (tolerant) carry = carryNext;
            cancel.ThrowIfCancellationRequested();
            // Do not truncate a partially matched frame: keep the last valid stitched prefix.
            if (IsFull || d > maxRows - rows.Count) { result = Result.Full; return px; }
            if (d > h - top - bot) { result = Result.Lost; return px; }

            int n = d;
            List<int[]> fresh = new List<int[]>(n);
            List<int> from = new List<int>(n);
            for (int y = h - bot - d; y < h - bot - d + n; y++) { fresh.Add(Row(px, y)); from.Add(y); }
            lock (rows)
            {
                rows.InsertRange(rows.Count - bot, fresh);
                src.InsertRange(src.Count - bot, from);
                if (n == d)
                {
                    LearnFloats(px, d, top, h - bot);
                    Uncover(px, top, h - bot, bot);
                }
            }
            height = rows.Count;
            added = n;
            result = IsFull ? Result.Full : Result.Added;
            int[] old = prev;
            Keep(px, hs, flat);
            return old;
        }

        void Keep(int[] px, ulong[] hs, bool[] flat)
        {
            prev = px;
            prevHash = hs;
            prevFlat = flat;
            prevFeat = curFeatOf == px ? curFeat : null;
            curFeat = null;
            curFeatOf = null;
        }

        // Per cell of the overlap: pixels that stayed where they were although the page moved by d, and that the page
        // itself would have changed, belong to something floating over it. Two such scrolls in a row mark the cell.
        void LearnFloats(int[] px, int d, int from, int to)
        {
            for (int cy = from / Cell; cy * Cell < to - d && cy < ch; cy++)
            {
                for (int cx = 0; cx < cw; cx++)
                {
                    int informative = 0, stayed = 0, samples = 0;
                    for (int y = Math.Max(from, cy * Cell); y < Math.Min(to - d, cy * Cell + Cell); y++)
                    {
                        int o = y * w, os = (y + d) * w;
                        for (int x = cx * Cell; x < Math.Min(w, cx * Cell + Cell); x++)
                        {
                            samples++;
                            int now = px[o + x] & 0xFFFFFF, same = prev[o + x] & 0xFFFFFF, moved = prev[os + x] & 0xFFFFFF;
                            if (same == moved) continue;          // flat there: tells nothing
                            informative++;
                            if (now == same) stayed++;
                        }
                    }
                    int k = cy * cw + cx;
                    if (samples == 0 || informative * 5 < samples) continue;
                    if (stayed * 2 >= informative) { if (still[k] < 10) still[k]++; }
                    else still[k] = 0;
                }
            }
        }

        // Stitched rows whose copy came from under a floating cell get that cell repainted from this frame, where the
        // same content now shows clear of it. Frame row i is stitched row rows.Count - h + i (the footer stays last).
        // The cells around a floating one count too: its edges rarely fall on cell lines, and repainting plain page
        // content from another frame changes nothing.
        void Uncover(int[] px, int from, int to, int bot)
        {
            bool[] near = new bool[cw * ch];
            bool any = false;
            for (int cy = 0; cy < ch; cy++)
                for (int cx = 0; cx < cw; cx++)
                {
                    if (!Floating(cx, cy)) continue;
                    any = true;
                    for (int ny2 = Math.Max(0, cy - 1); ny2 <= Math.Min(ch - 1, cy + 1); ny2++)
                        for (int nx = Math.Max(0, cx - 1); nx <= Math.Min(cw - 1, cx + 1); nx++) near[ny2 * cw + nx] = true;
                }
            if (!any) return;
            int offset = rows.Count - h;
            for (int i = from; i < to; i++)
            {
                int c = offset + i;
                if (c < 0 || c >= rows.Count - bot) continue;
                int sy = src[c] / Cell, ny = i / Cell;
                if (sy >= ch) continue;
                for (int cx = 0; cx < cw; cx++)
                {
                    if (!near[sy * cw + cx] || near[ny * cw + cx]) continue;
                    long key = (long)c * cw + cx;
                    if (cleaned.Contains(key)) continue;
                    int[] r = rows[c];
                    int o = i * w;
                    for (int x = cx * Cell; x < Math.Min(w, cx * Cell + Cell); x++) r[x] = px[o + x] | unchecked((int)0xFF000000);
                    cleaned.Add(key);
                }
            }
        }

        // Fallback when no shift matches exactly: compares per-row brightness profiles for every shift and takes the
        // one that is clearly and uniquely best (a repeating pattern is refused). 0 nothing scrolled, -1 not found.
        // Smooth scrolling and display scaling move the page by fractions of a row, blending neighbouring rows: each
        // shift is also tried a fifth, two fifths... of a row further (the previous frame interpolated), and the result
        // is the nearest whole row.
        int Tolerant(int[] px, bool[] flat, int from, int to)
        {
            int n = to - from;
            if (n < 32) return -1;
            if (prevFeat == null) prevFeat = Features(prev);
            curFeat = Features(px);
            curFeatOf = px;
            int maxD = n - Math.Max(24, n / 8), step = n > 600 ? 2 : 1; // tall areas: every other row is enough
            // Either frame may be the blended one: the fraction is tried on the previous frame (a shift of d + f) and on
            // this one (d - f). Whole rows first for every shift; fractions only around the three best of them.
            const int Subs = 5;
            float[] cf = curFeat, pf = prevFeat;
            Func<int, int, double> errAt = delegate(int d, int s)
            {
                bool onPrev = s < Subs;
                double f = (onPrev ? s : s - Subs + 1) / (double)Subs, sum = 0;
                int considered = 0;
                for (int i = from; i < to - d; i += step)
                {
                    if (flat[i] && prevFlat[i + d]) continue;
                    int a0 = i * bins, a1 = Math.Min(h - 1, i + 1) * bins, b0 = (i + d) * bins, b1 = Math.Min(h - 1, i + d + 1) * bins;
                    double e = 0;
                    if (f == 0) for (int k = 0; k < bins; k++) e += Math.Abs(cf[a0 + k] - pf[b0 + k]);
                    else if (onPrev) for (int k = 0; k < bins; k++) e += Math.Abs(cf[a0 + k] - (pf[b0 + k] * (1 - f) + pf[b1 + k] * f));
                    else for (int k = 0; k < bins; k++) e += Math.Abs(cf[a0 + k] * (1 - f) + cf[a1 + k] * f - pf[b0 + k]);
                    sum += e / bins;
                    considered++;
                }
                return considered >= 16 ? sum / considered : double.MaxValue;
            };
            double[] err = new double[maxD + 1], shift = new double[maxD + 1];
            for (int d = 0; d <= maxD; d++) { if ((d & 15) == 0) cancel.ThrowIfCancellationRequested(); err[d] = errAt(d, 0); shift[d] = d; }
            List<int> near = new List<int>();
            for (int pick = 0; pick < 3; pick++)
            {
                int c = -1;
                for (int d = 0; d <= maxD; d++)
                    if (err[d] != double.MaxValue && !near.Contains(d) && (c < 0 || err[d] < err[c])) c = d;
                if (c < 0) break;
                near.Add(c);
            }
            foreach (int c in near.ToArray())
                for (int d = Math.Max(0, c - 1); d <= Math.Min(maxD, c + 1); d++)
                    for (int s = 1; s < 2 * Subs - 1; s++)
                    {
                        double es = errAt(d, s);
                        if (es < err[d]) { err[d] = es; shift[d] = s < Subs ? d + s / (double)Subs : d - (s - Subs + 1) / (double)Subs; }
                    }
            int best = -1, counted = 0;
            double bestErr = double.MaxValue, total = 0;
            for (int d = 0; d <= maxD; d++)
            {
                if (err[d] == double.MaxValue) continue;
                total += err[d];
                counted++;
                if (err[d] < bestErr) { bestErr = err[d]; best = d; }
            }
            if (best < 0 || counted < 8) return -1;
            if (bestErr > 20 || bestErr > total / counted * 0.3) return -1;
            for (int d = 0; d <= maxD; d++)
                if (Math.Abs(d - best) > 2 && err[d] < bestErr * 1.5 + 1) return -1;
            // The nearest whole row, counting what earlier frames left over; the rest waits for the next kept frame.
            double want = shift[best] + carry;
            int whole = Math.Max(0, (int)Math.Round(want, MidpointRounding.AwayFromZero));
            carryNext = Math.Max(-0.5, Math.Min(0.5, want - whole));
            return whole;
        }

        double carry, carryNext;   // fractions of a row (Tolerant)

        // Per row, the average brightness of each of the bins bands across the compared width.
        float[] Features(int[] px)
        {
            float[] f = new float[h * bins];
            for (int y = 0; y < h; y++)
            {
                if ((y & 31) == 0) cancel.ThrowIfCancellationRequested();
                int o = y * w;
                for (int b = 0; b < bins; b++)
                {
                    int x0 = b * hashW / bins, x1 = (b + 1) * hashW / bins, sum = 0;
                    for (int x = x0; x < x1; x++)
                    {
                        int p = px[o + x];
                        sum += ((p >> 16) & 255) * 3 + ((p >> 8) & 255) * 6 + (p & 255);
                    }
                    f[y * bins + b] = sum / (10f * Math.Max(1, x1 - x0));
                }
            }
            return f;
        }

        int[] Row(int[] px, int y)
        {
            int[] r = new int[w];
            Array.Copy(px, y * w, r, 0, w);
            for (int x = 0; x < w; x++) r[x] |= unchecked((int)0xFF000000); // screen copies have alpha = 0
            return r;
        }

        void HashRows(int[] px, ulong[] hs, bool[] flat)
        {
            for (int y = 0; y < h; y++)
            {
                if ((y & 31) == 0) cancel.ThrowIfCancellationRequested();
                int o = y * w, first = px[o] & 0xFFFFFF;
                ulong hash = 14695981039346656037UL;
                bool same = true;
                for (int x = 0; x < hashW; x++)
                {
                    int p = px[o + x] & 0xFFFFFF;
                    if (p != first) same = false;
                    hash = (hash ^ (uint)p) * 1099511628211UL;
                }
                hs[y] = hash;
                flat[y] = same;
            }
        }

        // Downward shift between the previous and current frame within [from, to): > 0 scrolled, 0 static, -1 not found
        // reliably.
        int BestShift(ulong[] hs, bool[] flat, int from, int to)
        {
            Dictionary<ulong, int> before = UniqueRows(prevHash, prevFlat, from, to);
            Dictionary<ulong, int> now = UniqueRows(hs, flat, from, to);
            Dictionary<int, int> votes = new Dictionary<int, int>();
            foreach (KeyValuePair<ulong, int> kv in now)
            {
                int j;
                if (kv.Value < 0 || !before.TryGetValue(kv.Key, out j) || j < 0) continue;
                int d = j - kv.Value, v;
                votes.TryGetValue(d, out v);
                votes[d] = v + 1;
            }
            int best = 0, bestVotes = 0;
            foreach (KeyValuePair<int, int> kv in votes)
            {
                if (kv.Value > bestVotes || (kv.Value == bestVotes && kv.Key == 0)) { best = kv.Key; bestVotes = kv.Value; }
            }
            if (bestVotes < 3) return Scan(hs, flat, from, to);
            if (best == 0) return 0;
            if (best < 0) return UpShift;

            // Confirmation: almost every non-flat row in the overlap must match.
            int considered = 0, matched = 0;
            for (int i = from; i < to - best; i++)
            {
                if (flat[i] && prevFlat[i + best]) continue;
                considered++;
                if (hs[i] == prevHash[i + best]) matched++;
            }
            if (considered < 4 || matched < considered * 0.8) return -1;
            return best;
        }

        // Fallback when voting finds too few unique rows: the shift whose overlap matches best, if clearly good.
        int Scan(ulong[] hs, bool[] flat, int from, int to)
        {
            int best = -1;
            double bestScore = 0.9;
            var scores = new List<(int Shift, double Score)>();
            for (int d = 1; d < to - from - 8; d++)
            {
                if ((d & 31) == 0) cancel.ThrowIfCancellationRequested();
                int considered = 0, matched = 0;
                for (int i = from; i < to - d; i++)
                {
                    if (flat[i] && prevFlat[i + d]) continue;
                    considered++;
                    if (hs[i] == prevHash[i + d]) matched++;
                }
                if (considered < 8) continue;
                double sc = (double)matched / considered;
                scores.Add((d,sc));
                if (sc > bestScore) { bestScore = sc; best = d; }
            }
            if (best > 0 && scores.Any(s => Math.Abs(s.Shift-best)>2 && s.Score >= bestScore-0.02)) return -1;
            return best;
        }

        // Hash -> row, for non-flat rows that occur once (repeated rows don't tell position).
        static Dictionary<ulong, int> UniqueRows(ulong[] hs, bool[] flat, int from, int to)
        {
            Dictionary<ulong, int> d = new Dictionary<ulong, int>();
            for (int i = from; i < to; i++)
            {
                if (flat[i]) continue;
                if (d.ContainsKey(hs[i])) d[hs[i]] = -1;
                else d[hs[i]] = i;
            }
            return d;
        }

        // A small copy of what is stitched so far, fitting maxW x maxH, for the live preview (any thread). Each pixel
        // averages a few samples of its block, so text stays a soft gray instead of noise. Null when nothing is there.
        public int[] Thumb(int maxW, int maxH, out int tw, out int th)
        {
            tw = th = 0;
            lock (rows)
            {
                int n = rows.Count;
                if (n == 0 || maxW < 1 || maxH < 1) return null;
                double k = Math.Min(1.0, Math.Min((double)maxW / w, (double)maxH / n));
                tw = Math.Max(1, (int)(w * k));
                th = Math.Max(1, (int)(n * k));
                int[] t = new int[tw * th];
                double step = 1 / k;
                for (int y = 0; y < th; y++)
                {
                    for (int x = 0; x < tw; x++)
                    {
                        int r = 0, g = 0, b = 0, count = 0;
                        for (int sy = 0; sy < 3; sy++)
                        {
                            int[] row = rows[Math.Min(n - 1, (int)((y + (sy + 0.5) / 3) * step))];
                            if (row == null) return null;
                            for (int sx = 0; sx < 3; sx++)
                            {
                                int p = row[Math.Min(w - 1, (int)((x + (sx + 0.5) / 3) * step))];
                                r += (p >> 16) & 255; g += (p >> 8) & 255; b += p & 255;
                                count++;
                            }
                        }
                        t[y * tw + x] = unchecked((int)0xFF000000) | (r / count << 16) | (g / count << 8) | (b / count);
                    }
                }
                return t;
            }
        }

        public Bitmap Compose()
        {
            if (composed || rows.Count == 0) throw new CaptureException("ERR_SCROLL_004");
            composed = true;
            Bitmap b;
            lock (rows)
            {
                b = new Bitmap(w, Math.Max(1, rows.Count), PixelFormat.Format32bppArgb);
                try
                {
                    BitmapData bd = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                    try
                    {
                        for (int y = 0; y < rows.Count; y++)
                        {
                            Marshal.Copy(rows[y], 0, new IntPtr(bd.Scan0.ToInt64() + (long)y * bd.Stride), w);
                            rows[y] = null; // release rows as they are copied to lower peak memory
                        }
                    }
                    finally { b.UnlockBits(bd); }
                }
                catch { b.Dispose(); throw; }
                rows.Clear();
                src.Clear();
                cleaned.Clear();
                prev = null; prevHash = null; prevFlat = null; prevFeat = null; curFeat = null; curFeatOf = null;
            }
            return b;
        }
    }

}
