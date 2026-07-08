using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace LayaAir3.Converter
{
    /// <summary>
    /// 轻量 JSON DOM，忠实模仿 JS 的对象字面量语义 + JSON.stringify(x, null, 2) 输出。
    ///
    /// 为什么不直接用插件的 JSONObject：
    ///   1. JSONObject.Print 不转义字符串（AppendFormat("\"{0}\"", str)）——蓝图节点的 propertyVal.code
    ///      字段含换行/引号等 GLSL 文本，直接输出会产出非法 JSON。
    ///   2. JSONObject 按 \t 缩进、数字走当前区域文化 ToString()（可能出逗号/科学计数）。
    ///   3. C# 版蓝图转换器要与 JS 版产物按语义 diff 对齐，一个模仿 JSON.stringify 的写出器更可控。
    ///
    /// 输入侧（.shadergraph）仍用插件 JSONObject 解析，再 From() 转成 Jval 统一处理。
    /// 对象保持插入顺序（有序），数组保持顺序；两者都可读、可写、可原地修改。
    /// </summary>
    public class Jval
    {
        public enum JType { Null, Bool, Number, String, Array, Object }

        public JType Type { get; private set; }

        // 基元
        public bool Bool;
        public double Num;      // 数值统一用 double 存储（整数在输出时不带小数点）
        public string Str;

        // 数组
        private List<Jval> _arr;
        // 对象（有序 key/value）
        private List<string> _keys;
        private List<Jval> _vals;

        // ── 工厂 ───────────────────────────────────────────────
        public static Jval Obj() { return new Jval { Type = JType.Object, _keys = new List<string>(), _vals = new List<Jval>() }; }
        public static Jval Arr() { return new Jval { Type = JType.Array, _arr = new List<Jval>() }; }
        public static Jval Arr(params Jval[] items)
        {
            var a = Arr();
            foreach (var it in items) a._arr.Add(it ?? Null());
            return a;
        }
        public static Jval Null() { return new Jval { Type = JType.Null }; }
        public static Jval Of(bool b) { return new Jval { Type = JType.Bool, Bool = b }; }
        public static Jval Of(double n) { return new Jval { Type = JType.Number, Num = n }; }
        public static Jval Of(int n) { return new Jval { Type = JType.Number, Num = n }; }
        public static Jval Of(string s) { return s == null ? Null() : new Jval { Type = JType.String, Str = s }; }

        // ── 谓词 ───────────────────────────────────────────────
        public bool IsObject { get { return Type == JType.Object; } }
        public bool IsArray { get { return Type == JType.Array; } }
        public bool IsNull { get { return Type == JType.Null; } }
        public bool IsNumber { get { return Type == JType.Number; } }
        public bool IsString { get { return Type == JType.String; } }
        public bool IsBool { get { return Type == JType.Bool; } }

        public int Count { get { return IsArray ? _arr.Count : (IsObject ? _vals.Count : 0); } }

        // ── 对象访问 ───────────────────────────────────────────
        /// <summary>取字段；不存在返回 null（模仿 JS 的 undefined，配合 ?. 语义用 helper 判空）。</summary>
        public Jval Get(string key)
        {
            if (!IsObject) return null;
            int idx = _keys.IndexOf(key);
            return idx < 0 ? null : _vals[idx];
        }

        public bool Has(string key) { return IsObject && _keys.IndexOf(key) >= 0; }

        /// <summary>fluent 设置字段（存在则覆盖），返回自身。</summary>
        public Jval Set(string key, Jval v)
        {
            if (!IsObject) throw new InvalidOperationException("Set on non-object");
            v = v ?? Null();
            int idx = _keys.IndexOf(key);
            if (idx >= 0) { _vals[idx] = v; }
            else { _keys.Add(key); _vals.Add(v); }
            return this;
        }
        public Jval Set(string key, bool v) { return Set(key, Of(v)); }
        public Jval Set(string key, double v) { return Set(key, Of(v)); }
        public Jval Set(string key, int v) { return Set(key, Of(v)); }
        public Jval Set(string key, string v) { return Set(key, Of(v)); }

        public void Remove(string key)
        {
            if (!IsObject) return;
            int idx = _keys.IndexOf(key);
            if (idx >= 0) { _keys.RemoveAt(idx); _vals.RemoveAt(idx); }
        }

        public IEnumerable<string> Keys { get { return _keys; } }

        // ── 数组访问 ───────────────────────────────────────────
        public List<Jval> Items { get { return _arr; } }
        public Jval At(int i) { return (IsArray && i >= 0 && i < _arr.Count) ? _arr[i] : null; }
        public Jval Push(Jval v) { if (!IsArray) throw new InvalidOperationException("Push on non-array"); _arr.Add(v ?? Null()); return this; }

        // ── 基元读取（带默认值，模仿 JS 宽松取值） ─────────────
        public double AsNum(double dflt = 0) { return IsNumber ? Num : dflt; }
        public bool AsBool(bool dflt = false) { return IsBool ? Bool : (IsNumber ? Num != 0 : dflt); }
        public string AsStr(string dflt = null) { return IsString ? Str : dflt; }
        public int AsInt(int dflt = 0) { return IsNumber ? (int)Math.Round(Num) : dflt; }

        // 便捷：字段直读
        public double NumOf(string key, double dflt = 0) { var v = Get(key); return v != null && v.IsNumber ? v.Num : dflt; }
        public string StrOf(string key, string dflt = null) { var v = Get(key); return v != null && v.IsString ? v.Str : dflt; }
        public bool BoolOf(string key, bool dflt = false) { var v = Get(key); return v != null ? v.AsBool(dflt) : dflt; }

        // ── 输入侧：自研 JSON 解析器（标准 JSON，递归下降） ────
        // 不用插件 JSONObject：其 type/list 字段 private 无法转换，且此处需完全掌控数值/转义。
        /// <summary>解析标准 JSON 文本为 Jval DOM。</summary>
        public static Jval Parse(string text)
        {
            if (text == null) return Null();
            int pos = 0;
            var v = ParseValue(text, ref pos);
            return v;
        }

        private static void SkipWs(string s, ref int i)
        {
            while (i < s.Length)
            {
                char c = s[i];
                if (c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == '﻿') i++;
                else break;
            }
        }

        private static Jval ParseValue(string s, ref int i)
        {
            SkipWs(s, ref i);
            if (i >= s.Length) return Null();
            char c = s[i];
            switch (c)
            {
                case '{': return ParseObject(s, ref i);
                case '[': return ParseArray(s, ref i);
                case '"': return Of(ParseString(s, ref i));
                case 't': i += 4; return Of(true);       // true
                case 'f': i += 5; return Of(false);      // false
                case 'n': i += 4; return Null();         // null
                default: return ParseNumber(s, ref i);
            }
        }

        private static Jval ParseObject(string s, ref int i)
        {
            var o = Obj();
            i++; // {
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return o; }
            while (i < s.Length)
            {
                SkipWs(s, ref i);
                string key = ParseString(s, ref i);
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ':') i++;
                var val = ParseValue(s, ref i);
                o.Set(key, val);
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == '}') { i++; break; }
                break;
            }
            return o;
        }

        private static Jval ParseArray(string s, ref int i)
        {
            var a = Arr();
            i++; // [
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return a; }
            while (i < s.Length)
            {
                var val = ParseValue(s, ref i);
                a._arr.Add(val);
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == ']') { i++; break; }
                break;
            }
            return a;
        }

        private static string ParseString(string s, ref int i)
        {
            if (i >= s.Length || s[i] != '"') return "";
            i++; // opening quote
            var sb = new StringBuilder();
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') break;
                if (c == '\\' && i < s.Length)
                {
                    char e = s[i++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'u':
                            if (i + 4 <= s.Length)
                            {
                                int code = int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                                sb.Append((char)code);
                                i += 4;
                            }
                            break;
                        default: sb.Append(e); break;
                    }
                }
                else sb.Append(c);
            }
            return sb.ToString();
        }

        private static Jval ParseNumber(string s, ref int i)
        {
            int start = i;
            while (i < s.Length)
            {
                char c = s[i];
                if ((c >= '0' && c <= '9') || c == '-' || c == '+' || c == '.' || c == 'e' || c == 'E') i++;
                else break;
            }
            string num = s.Substring(start, i - start);
            double d;
            if (double.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return Of(d);
            return Of(0);
        }

        // ── 输出：模仿 JSON.stringify(x, null, 2) ───────────────
        /// <summary>序列化为 JSON 文本（对齐 JS JSON.stringify(x, null, indent)）。</summary>
        public string Serialize(int indent = 2)
        {
            var sb = new StringBuilder();
            Write(sb, indent, 0);
            return sb.ToString();
        }

        private void Write(StringBuilder sb, int indent, int depth)
        {
            switch (Type)
            {
                case JType.Null: sb.Append("null"); break;
                case JType.Bool: sb.Append(Bool ? "true" : "false"); break;
                case JType.Number: sb.Append(FormatNumber(Num)); break;
                case JType.String: WriteString(sb, Str); break;
                case JType.Array:
                    if (_arr.Count == 0) { sb.Append("[]"); break; }
                    sb.Append('[');
                    for (int i = 0; i < _arr.Count; i++)
                    {
                        NewlineIndent(sb, indent, depth + 1);
                        _arr[i].Write(sb, indent, depth + 1);
                        if (i < _arr.Count - 1) sb.Append(',');
                    }
                    NewlineIndent(sb, indent, depth);
                    sb.Append(']');
                    break;
                case JType.Object:
                    if (_vals.Count == 0) { sb.Append("{}"); break; }
                    sb.Append('{');
                    for (int i = 0; i < _vals.Count; i++)
                    {
                        NewlineIndent(sb, indent, depth + 1);
                        WriteString(sb, _keys[i]);
                        sb.Append(indent > 0 ? ": " : ":");
                        _vals[i].Write(sb, indent, depth + 1);
                        if (i < _vals.Count - 1) sb.Append(',');
                    }
                    NewlineIndent(sb, indent, depth);
                    sb.Append('}');
                    break;
            }
        }

        private static void NewlineIndent(StringBuilder sb, int indent, int depth)
        {
            if (indent <= 0) return;
            sb.Append('\n');
            sb.Append(' ', indent * depth);
        }

        // JS JSON.stringify 的数字规则：整数不带小数点，其余用可 round-trip 的最短表示。
        public static string FormatNumber(double n)
        {
            if (double.IsNaN(n) || double.IsInfinity(n)) return "null"; // JS stringify 把它们变 null
            // 快路径只到 2^53：以内的整数精确可表示，其最短表示就是十进制原文；
            // 超过 2^53 的整数 JS 也走最短 round-trip（如 303189532608317952 → "303189532608317950"）。
            if (n == Math.Floor(n) && Math.Abs(n) <= 9007199254740992.0)
                return ((long)n).ToString(CultureInfo.InvariantCulture);
            return ShortestRoundTrip(n);
        }

        // 最短 round-trip 十进制表示，对齐 JS Number→String（ECMAScript Number::toString）。
        //
        // 不能信 Mono 的 G/R 格式化：其取整方向在部分值上是错的（如 0.9600061178207397 的
        // G16 被印成 …98——两串虽都 round-trip 到同一 double，但 V8 按「离精确值更近」选 …97，
        // 逐字节对拍就差一位）。这里自算：BigInteger 求 double 的精确十进制展开 →
        // 从 1 位有效数字起取「正确舍入」前缀，第一个能 round-trip 回原 double 的即 JS 的答案，
        // 再按 ECMAScript 规范排版（含 1e21/1e-7 等指数式，与 JS 的 "1e+21"/"1e-7" 一致）。
        public static string ShortestRoundTrip(double n)
        {
            bool neg = n < 0;
            double abs = Math.Abs(n);
            long bits = BitConverter.DoubleToInt64Bits(abs);
            int biasedExp = (int)((bits >> 52) & 0x7FF);
            long frac = bits & 0xFFFFFFFFFFFFFL;
            long mant; int e2;
            if (biasedExp == 0) { mant = frac; e2 = -1074; }                 // subnormal
            else { mant = frac | (1L << 52); e2 = biasedExp - 1075; }        // normal（含隐含位）
            // abs = mant × 2^e2 的精确十进制展开：数字串 digitsFull，值 = 0.digitsFull × 10^decExp
            string digitsFull; int decExp;
            if (e2 >= 0)
            {
                digitsFull = (new System.Numerics.BigInteger(mant) << e2).ToString();
                decExp = digitsFull.Length;
            }
            else
            {
                // mant × 2^e2 = (mant × 5^-e2) × 10^e2
                digitsFull = (new System.Numerics.BigInteger(mant) * System.Numerics.BigInteger.Pow(5, -e2)).ToString();
                decExp = digitsFull.Length + e2;
            }

            for (int p = 1; p <= 17; p++)
            {
                // 取 digitsFull 的正确舍入 p 位前缀（最近舍入；恰好半时向偶数——实际几乎不出现）
                string c; int k = decExp;
                if (digitsFull.Length <= p) { c = digitsFull; }
                else
                {
                    c = digitsFull.Substring(0, p);
                    char next = digitsFull[p];
                    bool roundUp;
                    if (next > '5') roundUp = true;
                    else if (next < '5') roundUp = false;
                    else
                    {
                        bool restNonZero = false;
                        for (int i = p + 1; i < digitsFull.Length; i++) if (digitsFull[i] != '0') { restNonZero = true; break; }
                        roundUp = restNonZero || ((c[p - 1] - '0') % 2 == 1);   // 半时向偶
                    }
                    if (roundUp)
                    {
                        var arr = c.ToCharArray();
                        int i2 = p - 1;
                        while (i2 >= 0 && arr[i2] == '9') { arr[i2] = '0'; i2--; }
                        if (i2 < 0) { c = "1" + new string(arr); k++; c = c.Substring(0, p); }   // 999→1000：进位后仍取 p 位
                        else { arr[i2]++; c = new string(arr); }
                    }
                }
                // 去掉尾零得有效数字串 s
                int end = c.Length; while (end > 1 && c[end - 1] == '0') end--;
                string s = c.Substring(0, end);
                // round-trip 判定用精确整数比较（不能信 Mono 的 double.Parse——极端指数下它的
                // 最近舍入也会错一 ulp，会误收太短的候选）
                if (RoundTripsExact(System.Numerics.BigInteger.Parse(s), k - s.Length, mant, e2, biasedExp))
                {
                    string formatted = EcmaFormat(s, k);
                    return neg ? "-" + formatted : formatted;
                }
            }
            // 理论上到不了这里（17 位必 round-trip）；兜底走 R
            return n.ToString("R", CultureInfo.InvariantCulture);
        }

        // 精确 round-trip 判定：候选 c = C × 10^ec 按「最近舍入、半时向偶」解析是否回到 d = m × 2^e2。
        // 即 c 落在 (d - lo, d + hi) 内；恰好落在半距边界时，仅当 m 为偶数才归属 d。
        //   hi 半距 = 2^(e2-1)；lo 半距一般也是 2^(e2-1)，
        //   但 m = 2^52 且非最低规格化指数时（幂次边界，下方间隙减半）= 2^(e2-2)。
        private static bool RoundTripsExact(System.Numerics.BigInteger C, int ec, long m, int e2, int biasedExp)
        {
            bool even = (m & 1) == 0;
            // 上界：d + hi = (2m+1) × 2^(e2-1)
            int cmpHi = CompareC10VsB2(C, ec, 2 * m + 1, e2 - 1);
            if (cmpHi > 0 || (cmpHi == 0 && !even)) return false;
            // 下界：正常 d - lo = (2m-1) × 2^(e2-1)；幂次边界 d - lo = (4m-1) × 2^(e2-2)
            bool boundary = (m == (1L << 52)) && biasedExp > 1;
            int cmpLo = boundary ? CompareC10VsB2(C, ec, 4 * m - 1, e2 - 2)
                                 : CompareC10VsB2(C, ec, 2 * m - 1, e2 - 1);
            if (cmpLo < 0 || (cmpLo == 0 && !even)) return false;
            return true;
        }

        // 精确比较 C × 10^ec 与 b × 2^eb（把负指数乘到对侧化成整数比较）。
        private static int CompareC10VsB2(System.Numerics.BigInteger C, int ec, long b, int eb)
        {
            System.Numerics.BigInteger lhs = C, rhs = b;
            if (ec > 0) lhs *= System.Numerics.BigInteger.Pow(10, ec);
            else if (ec < 0) rhs *= System.Numerics.BigInteger.Pow(10, -ec);
            if (eb > 0) rhs <<= eb;
            else if (eb < 0) lhs <<= -eb;
            return lhs.CompareTo(rhs);
        }

        // ECMAScript Number::toString 排版：有效数字串 s（无尾零）+ 小数点位置 k（值 = 0.s × 10^k）
        private static string EcmaFormat(string s, int k)
        {
            int len = s.Length;
            if (len <= k && k <= 21) return s + new string('0', k - len);                 // 整数
            if (0 < k && k <= 21) return s.Substring(0, k) + "." + s.Substring(k);        // 小数点在中间
            if (-6 < k && k <= 0) return "0." + new string('0', -k) + s;                  // 0.000xxx
            // 指数式：d.ddd e±(k-1)
            string mantissa = len == 1 ? s : s.Substring(0, 1) + "." + s.Substring(1);
            int e = k - 1;
            return mantissa + "e" + (e >= 0 ? "+" : "-") + Math.Abs(e).ToString(CultureInfo.InvariantCulture);
        }

        // JSON 字符串转义（JS 标准）
        private static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            if (s != null)
            {
                foreach (char c in s)
                {
                    switch (c)
                    {
                        case '"': sb.Append("\\\""); break;
                        case '\\': sb.Append("\\\\"); break;
                        case '\n': sb.Append("\\n"); break;
                        case '\r': sb.Append("\\r"); break;
                        case '\t': sb.Append("\\t"); break;
                        case '\b': sb.Append("\\b"); break;
                        case '\f': sb.Append("\\f"); break;
                        default:
                            if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                            else sb.Append(c);
                            break;
                    }
                }
            }
            sb.Append('"');
        }
    }
}
