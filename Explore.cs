// ============================================================================
//  只读内存探索器：dump DataHolder / MainCharStateControl / SaveData 尾部等。
//  绝不写入。输出 JSON 行到 stdout。
//  构建:
//    C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /codepage:65001
//      /target:exe /platform:x64 /optimize+ /out:temp\Explore.exe Explore.cs
// ============================================================================
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace LilliaTrainer
{
    internal static class E
    {
        internal const string ProcessName = "NoviceSuccubusLillia";
        internal const long AssemblySize = 55217152L;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadProcessMemory(IntPtr h, IntPtr addr, byte[] buf, IntPtr size, out IntPtr read);

        private static IntPtr h;

        private static bool Read(ulong addr, byte[] buf, int len)
        {
            if (addr < 0x10000UL || addr > 0x00007FFFFFFEFFFFUL) return false;
            IntPtr got;
            if (!ReadProcessMemory(h, new IntPtr(unchecked((long)addr)), buf, new IntPtr(len), out got)) return false;
            return got.ToInt64() == len;
        }

        private static ulong P(ulong a)
        {
            byte[] b = new byte[8];
            if (!Read(a, b, 8)) return 0;
            return BitConverter.ToUInt64(b, 0);
        }

        private static int I32(ulong a)
        {
            byte[] b = new byte[4];
            if (!Read(a, b, 4)) return 0;
            return BitConverter.ToInt32(b, 0);
        }

        private static float F32(ulong a)
        {
            byte[] b = new byte[4];
            if (!Read(a, b, 4)) return 0;
            return BitConverter.ToSingle(b, 0);
        }

        private static double F64(ulong a)
        {
            byte[] b = new byte[8];
            if (!Read(a, b, 8)) return 0;
            return BitConverter.ToDouble(b, 0);
        }

        private static string CStr(ulong a, int max)
        {
            if (a < 0x10000UL || a > 0x00007FFFFFFEFFFFUL) return null;
            byte[] b = new byte[max];
            if (!Read(a, b, max)) return null;
            int n = Array.IndexOf(b, (byte)0);
            if (n <= 0) return null;
            return Encoding.ASCII.GetString(b, 0, n);
        }

        private static string ClassName(ulong obj)
        {
            ulong k = P(obj);
            if (k < 0x10000UL || k > 0x00007FFFFFFEFFFFUL) return null;
            return CStr(P(k + 0x10), 96);
        }

        private static string Str(ulong s)
        {
            if (s < 0x10000UL || s > 0x00007FFFFFFEFFFFUL) return null;
            int len = I32(s + 0x10);
            if (len < 0 || len > 256) return null;
            if (len == 0) return "";
            byte[] b = new byte[len * 2];
            if (!Read(s + 0x14, b, len * 2)) return null;
            return Encoding.Unicode.GetString(b, 0, len * 2);
        }

        private static string J(string s)
        {
            if (s == null) return "null";
            StringBuilder sb = new StringBuilder("\"");
            foreach (char c in s)
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c < 32) sb.Append(' ');
                else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }

        // dump 一个对象的一段偏移：指针→类名+首字段，int/float 猜测
        private static void DumpObject(string tag, ulong obj, int from, int to)
        {
            Console.WriteLine("{\"tag\":" + J(tag) + ",\"addr\":\"0x" + obj.ToString("X") + "\"" +
                (ClassName(obj) == null ? "" : ",\"class\":" + J(ClassName(obj))) + ",\"fields\":[");
            bool first = true;
            for (int off = from; off < to; off += 4)
            {
                ulong a = obj + (ulong)off;
                byte[] b = new byte[8];
                string desc = null; string kind = "raw";
                if (Read(a, b, 8))
                {
                    ulong pv = BitConverter.ToUInt64(b, 0);
                    if (pv >= 0x10000UL && pv <= 0x00007FFFFFFEFFFFUL && (pv & 3) == 0)
                    {
                        string cn = ClassName(pv);
                        if (cn != null) { kind = "obj"; desc = cn; }
                        else
                        {
                            ulong arrLen = P(pv + 0x18);
                            if (arrLen > 0 && arrLen < 4096)
                            {
                                // 数组特征：0x18 是 length（<64bit 数组头 0x20 data）
                                ulong el0 = P(pv + 0x20);
                                if (el0 == 0 || (el0 >= 0x10000UL && el0 <= 0x00007FFFFFFEFFFFUL))
                                { kind = "arr?"; desc = "len=" + arrLen; }
                            }
                        }
                    }
                    if (desc == null)
                    {
                        int iv = BitConverter.ToInt32(b, 0);
                        float fv = BitConverter.ToSingle(b, 0);
                        kind = "i32/f32";
                        if (iv >= -1000000 && iv <= 100000000 && iv != 0) desc = "i=" + iv;
                        if (Math.Abs(fv) > 0.001f && Math.Abs(fv) < 1e7f) desc = (desc == null ? "" : desc + " ") + "f=" + fv.ToString("0.###", CultureInfo.InvariantCulture);
                    }
                }
                if (desc != null)
                {
                    if (!first) Console.WriteLine(",");
                    first = false;
                    Console.Write("{\"off\":\"0x" + off.ToString("X") + "\",\"kind\":" + J(kind) + ",\"v\":" + J(desc) + "}");
                }
            }
            Console.WriteLine("]}");
        }

        // 找到对象里的 List`1 / 数组字段，转储元素（指针元素→类名，值元素→原始 int）
        private static void DumpCollections(string tag, ulong obj, int from, int to)
        {
            for (int off = from; off < to; off += 8)
            {
                ulong p = P(obj + (ulong)off);
                if (p < 0x10000UL || p > 0x00007FFFFFFEFFFFUL) continue;
                string cn = ClassName(p);
                if (cn == null) continue;
                if (cn.IndexOf("List`1", StringComparison.Ordinal) >= 0)
                {
                    ulong items = P(p + 0x10);
                    int size = I32(p + 0x18);
                    if (items != 0 && size > 0 && size < 4096)
                    {
                        Console.WriteLine("{\"coll\":" + J(tag + ".list@0x" + off.ToString("X")) + ",\"size\":" + size + "}");
                        DumpPtrArray("list@" + off.ToString("X"), items, Math.Min(size, 96));
                    }
                }
                else if (cn.EndsWith("[]", StringComparison.Ordinal))
                {
                    ulong len = P(p + 0x18);
                    if (len > 0 && len < 4096) DumpPtrArray("arr@" + off.ToString("X"), p, (int)Math.Min(len, 96));
                }
                else if (cn.IndexOf("Dictionary`2", StringComparison.Ordinal) >= 0)
                {
                    Console.WriteLine("{\"coll\":" + J(tag + ".dict@0x" + off.ToString("X")) + "}");
                    DumpDictionary("dict@" + off.ToString("X"), p);
                }
            }
        }

        // Dictionary`2: 探测字段，找 Entry[] 数组并 hex 转储前若干项
        private static void DumpDictionary(string tag, ulong dict)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("{\"dictTag\":").Append(J(tag)).Append(",\"addr\":\"0x").Append(dict.ToString("X")).Append("\",\"fields\":[");
            bool first = true;
            ulong entriesArr = 0; string entriesName = null;
            for (int off = 0x10; off <= 0x48; off += 8)
            {
                ulong p = P(dict + (ulong)off);
                if (p >= 0x10000UL && p <= 0x00007FFFFFFEFFFFUL)
                {
                    string cn = ClassName(p);
                    if (cn != null)
                    {
                        if (!first) sb.Append(",");
                        first = false;
                        if (cn.EndsWith("[]", StringComparison.Ordinal))
                        {
                            ulong l = P(p + 0x18);
                            sb.Append("{\"off\":\"0x").Append(off.ToString("X")).Append("\",\"arr\":").Append(J(cn)).Append(",\"len\":").Append(l).Append("}");
                            if (entriesArr == 0 || (cn.IndexOf("Entry", StringComparison.Ordinal) >= 0)) { entriesArr = p; entriesName = cn; }
                        }
                        else sb.Append("{\"off\":\"0x").Append(off.ToString("X")).Append("\",\"obj\":").Append(J(cn)).Append("}");
                    }
                }
                int iv = I32(dict + (ulong)off);
                if (iv != 0 && iv > -1000000 && iv < 1000000)
                {
                    if (!first) sb.Append(",");
                    first = false;
                    sb.Append("{\"off\":\"0x").Append(off.ToString("X")).Append("\",\"i\":").Append(iv).Append("}");
                }
            }
            sb.Append("]");
            if (entriesArr != 0)
            {
                ulong len = P(entriesArr + 0x18);
                int n = (int)Math.Min(len, 24UL);
                sb.Append(",\"entries\":{\"name\":").Append(J(entriesName)).Append(",\"len\":").Append(len).Append(",\"hex\":[");
                for (int i = 0; i < n; i++)
                {
                    byte[] eb = new byte[32];
                    if (Read(entriesArr + 0x20 + (ulong)(i * 32), eb, 32))
                    {
                        if (i > 0) sb.Append(",");
                        StringBuilder hb = new StringBuilder();
                        for (int k = 0; k < 32; k += 4)
                        {
                            if (k > 0) hb.Append(' ');
                            hb.Append(BitConverter.ToInt32(eb, k).ToString("X8", CultureInfo.InvariantCulture));
                        }
                        sb.Append(J(hb.ToString()));
                    }
                }
                sb.Append("]}");
            }
            sb.Append("}");
            Console.WriteLine(sb.ToString());
        }

        // dump 数组内容（指针数组，逐元素类名）
        private static void DumpPtrArray(string tag, ulong arr, int maxElem)
        {
            ulong len = P(arr + 0x18);
            StringBuilder sb = new StringBuilder();
            sb.Append("{\"tag\":").Append(J(tag)).Append(",\"len\":").Append(len).Append(",\"elems\":[");
            int n = (int)Math.Min(len, (ulong)maxElem);
            bool first = true;
            for (int i = 0; i < n; i++)
            {
                ulong p = P(arr + 0x20 + (ulong)(i * 8));
                string cn = p == 0 ? "null" : ClassName(p);
                if (cn == null) continue;
                if (!first) sb.Append(",");
                first = false;
                sb.Append("{\"i\":").Append(i).Append(",\"p\":\"0x").Append(p.ToString("X")).Append("\",\"cls\":").Append(J(cn)).Append("}");
            }
            sb.Append("]}");
            Console.WriteLine(sb.ToString());
        }

        private static int Main(string[] args)
        {
            try
            {
                ConOut();
                string what = args.Length > 0 ? args[0] : "all";

                Process target = null;
                // 从 exe 所在目录向上最多 4 层，任一层等于游戏 exe 目录即命中
                List<string> cands = new List<string>();
                string cur = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
                for (int i = 0; i < 4 && !string.IsNullOrEmpty(cur); i++)
                {
                    cands.Add(cur);
                    cur = Path.GetDirectoryName(cur);
                }
                foreach (Process p in Process.GetProcessesByName(ProcessName))
                {
                    try
                    {
                        string d = Path.GetDirectoryName(p.MainModule.FileName).TrimEnd('\\');
                        foreach (string c in cands)
                            if (string.Equals(d, c, StringComparison.OrdinalIgnoreCase)) { target = p; break; }
                        if (target != null) break;
                    }
                    catch { }
                }
                if (target == null) { Console.WriteLine("{\"error\":\"game not running\"}"); return 2; }
                h = OpenProcess(0x0410, false, target.Id);
                if (h == IntPtr.Zero) { Console.WriteLine("{\"error\":\"openprocess failed\"}"); return 3; }

                ulong ga = 0;
                foreach (ProcessModule mod in target.Modules)
                    if (string.Equals(mod.ModuleName, "GameAssembly.dll", StringComparison.OrdinalIgnoreCase))
                    { ga = unchecked((ulong)mod.BaseAddress.ToInt64()); break; }
                if (ga == 0) { Console.WriteLine("{\"error\":\"no GameAssembly\"}"); return 4; }

                // 与 ChineseTrainer 相同的静态链
                ulong method = P(ga + 0x2FD1D58UL);
                ulong klass = P(method + 0x20);
                ulong rgctx = P(klass + 0xC0);
                ulong sing = P(rgctx + 0x10);
                ulong stat = P(sing + 0xB8);
                ulong holder = P(stat);
                if (holder == 0) { Console.WriteLine("{\"error\":\"no holder\"}"); return 5; }
                ulong save = P(holder + 0x60);
                ulong mainChar = P(holder + 0x128);

                Console.WriteLine("{\"holder\":\"0x" + holder.ToString("X") + "\",\"save\":\"0x" + save.ToString("X") +
                    "\",\"mainChar\":\"0x" + mainChar.ToString("X") + "\"}");

                if (what == "all" || what == "holder") DumpObject("DataHolder", holder, 0x00, 0x400);
                if (what == "all" || what == "save") DumpObject("SaveData.tail", save, 0x168, 0x400);
                if ((what == "all" || what == "char") && mainChar != 0) DumpObject("MainChar", mainChar, 0x00, 0x600);

                if (what == "dress")
                {
                    ulong dress = P(holder + 0x130);
                    Console.WriteLine("{\"dress\":\"0x" + dress.ToString("X") + "\",\"class\":" + J(ClassName(dress)) + "}");
                    DumpObject("DressManager", dress, 0x00, 0x300);
                    // 递归一层：DressManager 里的集合元素
                    DumpCollections("dress", dress, 0x00, 0x300);
                }

                if (what == "saveraw")
                {
                    // SaveData 0x168-0x1A0 原始 int 转储（判断对象真实边界）
                    StringBuilder sb2 = new StringBuilder("{\"saveraw\":[");
                    bool f = true;
                    for (int off = 0x168; off < 0x1A0; off += 4)
                    {
                        if (!f) sb2.Append(",");
                        f = false;
                        sb2.Append("{\"off\":\"0x").Append(off.ToString("X")).Append("\",\"i\":").Append(I32(save + (ulong)off))
                            .Append(",\"f\":").Append(F32(save + (ulong)off).ToString("0.###", CultureInfo.InvariantCulture)).Append("}");
                    }
                    sb2.Append("]}");
                    Console.WriteLine(sb2.ToString());
                }

                if (what == "dict" && args.Length > 1)
                {
                    ulong baseObj = args[1] == "holder" ? holder : (args[1] == "save" ? save : (args[1] == "char" ? mainChar : 0));
                    int off = Convert.ToInt32(args[2], 16);
                    ulong dict = P(baseObj + (ulong)off);
                    DumpDictionary("dict@0x" + off.ToString("X"), dict);
                }

                if (what == "holderq")
                {
                    for (int off = 0x00; off < 0x400; off += 8)
                    {
                        ulong p = P(holder + (ulong)off);
                        if (p < 0x10000UL) continue;
                        string cn = ClassName(p);
                        if (cn == null) continue;
                        if (cn.IndexOf("Dictionary`2", StringComparison.Ordinal) >= 0)
                        {
                            Console.WriteLine("{\"holderOff\":\"0x" + off.ToString("X") + "\",\"dict\":\"0x" + p.ToString("X") + "\"}");
                            DumpDictionary("holder.dict@0x" + off.ToString("X"), p);
                        }
                    }
                }

                if (what == "arr" && args.Length > 2)
                {
                    // arr <base:holder|save|char> <hexoff> [max]
                    ulong baseObj = args[1] == "holder" ? holder : (args[1] == "save" ? save : (args[1] == "char" ? mainChar : ulong.Parse(args[1].StartsWith("0x", StringComparison.Ordinal) ? args[1].Substring(2) : args[1], NumberStyles.HexNumber)));
                    int off = Convert.ToInt32(args[2], 16);
                    int maxE = args.Length > 3 ? int.Parse(args[3], CultureInfo.InvariantCulture) : 64;
                    ulong arr = P(baseObj + (ulong)off);
                    DumpPtrArray("arr@0x" + off.ToString("X"), arr, maxE);
                }

                if (what == "obj" && args.Length > 2)
                {
                    // obj <hexaddr> <size>  深度转储任意对象
                    ulong a = ulong.Parse(args[1].StartsWith("0x", StringComparison.Ordinal) ? args[1].Substring(2) : args[1], NumberStyles.HexNumber);
                    int size = args.Length > 2 ? Convert.ToInt32(args[2], 16) : 0x200;
                    DumpObject("obj@0x" + a.ToString("X"), a, 0x00, size);
                    DumpCollections("obj", a, 0x00, size);
                }
                if (what == "holderq")
                {
                    // DataHolder 内所有 List/数组字段细节
                    for (int off = 0x00; off < 0x400; off += 8)
                    {
                        ulong p = P(holder + (ulong)off);
                        if (p < 0x10000UL) continue;
                        string cn = ClassName(p);
                        if (cn == null) continue;
                        if (cn.IndexOf("List`1", StringComparison.Ordinal) >= 0)
                        {
                            ulong items = P(p + 0x10);
                            ulong size = (ulong)I32(p + 0x18);
                            ulong arr = items != 0 ? items : 0;
                            if (arr != 0)
                            {
                                Console.WriteLine("{\"holderOff\":\"0x" + off.ToString("X") + "\",\"listSize\":" + size + "}");
                                DumpPtrArray("holder.list@0x" + off.ToString("X"), arr, 64);
                            }
                        }
                        else if (cn.IndexOf("[]", StringComparison.Ordinal) >= 0)
                        {
                            Console.WriteLine("{\"holderOff\":\"0x" + off.ToString("X") + "\",\"cls\":" + J(cn) + "}");
                        }
                    }
                }
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("{\"error\":" + J(ex.Message) + "}");
                return 1;
            }
        }

        private static void ConOut()
        {
            try
            {
                Console.OutputEncoding = Encoding.UTF8;
            }
            catch { }
        }
    }
}
