// ============================================================================
//  莉莉娅中文修改器 v2 (Novice Succubus Lillia - real-time memory trainer)
//  独立 WinForms 单文件程序，无第三方依赖。
//
//  v2 变化:
//    - UI 全面重设计：侧边栏导航 + 卡片式布局 + 概览仪表盘
//    - 新增「服装」页：36 系列服装图鉴（提取自游戏 catalog.json）、
//      拥有状态对比、未解锁清单、当前穿搭与发型查看
//    - 背包新增「移到背包 / 移到仓库」（修改既有容器字段，安全）
//
//  构建 (C#5 / .NET Framework 4.0 内置 csc):
//    set TEMP=<repo>\trainer\temp & set TMP=<repo>\trainer\temp
//    C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe -nologo -codepage:65001
//      -target:winexe -platform:x64 -optimize+ -out:..\莉莉娅中文修改器.exe
//      -reference:System.dll -reference:System.Windows.Forms.dll
//      -reference:System.Drawing.dll ChineseTrainer.cs
//
//  运行模式:
//    默认          = 图形界面 (实时只读轮询，用户点击应用/开关才写入)
//    --probe       = 只读，打印一行 JSON 描述当前存档
//    --selftest    = 用进程内合成内存对象测试读取/写入代码，绝不接触真实游戏
//    --preview     = 渲染界面到 trainer\preview.png
//
//  安全: 只操作既有基元字段与既有数组元素，不分配托管对象、不改写指针引用。
// ============================================================================
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;

namespace LilliaTrainer
{
    // ---------------------------------------------------------------- 常量
    internal static class Cfg
    {
        public const string ProcessName = "NoviceSuccubusLillia";
        public const string AssemblyName = "GameAssembly.dll";
        public const long AssemblySize = 55217152L;
        public const string AssemblySha256 = "F2D1139BA6B833CF89044896F34BF76F3897E985623A5563A5F6BF03C7FD86D0";
        public const int PollMs = 500;
        public const int MaxListItems = 2000;
        public const int MaxQuests = 500;
        public const int MaxArrayElems = 512;
    }

    internal static class Off
    {
        // 静态链
        public const int GaMethod = 0x2FD1D58;
        public const int MethodKlass = 0x20;
        public const int KlassRgctx = 0xC0;
        public const int RgctxSingletonKlass = 0x10;
        public const int SingletonStaticFields = 0xB8;
        public const int StaticFieldsHolder = 0x0;
        // holder (DataHolder)
        public const int HolderSave = 0x60;
        public const int HolderMainChar = 0x128;
        public const int HolderArousedLevel = 0x90;
        // MainCharStateControl
        public const int MainCharCurrentStamina = 0x80;
        public const int MainCharMaxStamina = 0x84;
        // SaveData 基元
        public const int Gold = 0x38;
        public const int CurrentDate = 0x3C;
        public const int Difficulty = 0x44;
        public const int TotalAchievements = 0x48;
        public const int CurrentSperm = 0x50;
        public const int CurrentBodySize = 0x58;
        public const int SelectedPresetIndex = 0x5C;
        public const int CurrentLevel = 0x60;
        public const int Inventory = 0x68;
        public const int QuestList = 0x98;
        public const int A0 = 0xA0, A8 = 0xA8, B0 = 0xB0, B8 = 0xB8, C0 = 0xC0, C8 = 0xC8, D0 = 0xD0;
        public const int MaxStamina = 0xE8;
        public const int FetishMask = 0xF8;
        public const int HasGroupedInventory = 0xFC;
        public const int LastTutorialGuideLevel = 0x100;
        public const int SeenScreenGuides = 0x104;
        public const int SexNpcTypeMask = 0x118;
        public const int TotalArrestCount = 0x11C;
        public const int MapChasedMask = 0x120;
        public const int MapNoiseAlertMask = 0x124;
        public const int MapUndetectedClearMask = 0x128;
        public const int OutfitChanged = 0x138;
        public const int BestOutingEjaculations = 0x148;
        public const int AchHistoryInit = 0x14C;
        public const int ViewedEndingMask = 0x154;
        public const int BestClearDays = 0x158;
        public const int PendingEndingChoice = 0x15C;
        public const int PendingEndingBit = 0x160;
        public const int LastEndingBit = 0x164;
        public const int NewGamePlusCount = 0x168;
        public const int InfinitePlay = 0x16C;
        public const int BunnyPoliceUnlocked = 0x16D;
        // InventoryData / List / 数组
        public const int InvList = 0x10;
        public const int ListItems = 0x10;
        public const int ListSize = 0x18;
        public const int ArrayLength = 0x18;
        public const int ArrayData = 0x20;
        // ItemData
        public const int ItemDatabaseId = 0x10;
        public const int ItemRawName = 0x18;
        public const int ItemType = 0x30;
        public const int ItemMaxCount = 0x38;
        public const int ItemX = 0x98;
        public const int ItemY = 0x9C;
        public const int ItemState = 0xA0;
        public const int ItemInvType = 0xA4;
        public const int ItemCount = 0xB8;
        public const int ItemPurchased = 0xBC;
        // QuestData
        public const int QuestId = 0x10;
        public const int QuestLevel = 0x18;
        public const int QuestRawName = 0x70;
        public const int QuestMaxCount = 0x80;
        public const int QuestCurrent = 0x84;
        // IL2CPP 对象 / 字符串
        public const int ObjectKlass = 0x0;
        public const int KlassName = 0x10;
        public const int StringLength = 0x10;
        public const int StringData = 0x14;
    }

    // ---------------------------------------------------------------- 地址
    internal static class Addr
    {
        public const ulong Min = 0x10000UL;
        public const ulong Max = 0x00007FFFFFFEFFFFUL;
        public static bool Valid(ulong a) { return a >= Min && a <= Max; }
        public static bool Range(ulong a, int len)
        {
            if (len <= 0) return false;
            if (!Valid(a)) return false;
            return a <= (Max - (ulong)len);
        }
    }

    internal static class Limits
    {
        public static bool I32(int v, int lo, int hi) { return v >= lo && v <= hi; }
        public static bool F32(float v, float lo, float hi)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return false;
            return v >= lo && v <= hi;
        }
        public static bool F64(double v, double lo, double hi)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return false;
            return v >= lo && v <= hi;
        }
    }

    // ---------------------------------------------------------------- 内存抽象
    internal interface IMemory
    {
        bool ReadBytes(ulong addr, byte[] buf, int off, int len);
        bool WriteBytes(ulong addr, byte[] buf, int off, int len);
    }

    internal sealed class ProcessMemory : IMemory, IDisposable
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "ReadProcessMemory")]
        private static extern bool ReadProcessMemory(IntPtr h, IntPtr addr, byte[] buf, IntPtr size, out IntPtr read);
        [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "WriteProcessMemory")]
        private static extern bool WriteProcessMemory(IntPtr h, IntPtr addr, byte[] buf, IntPtr size, out IntPtr read);

        public const uint AccessRead = 0x0410;   // QUERY_INFORMATION | VM_READ
        public const uint AccessWrite = 0x0438;  // QUERY_INFORMATION | VM_OPERATION | VM_WRITE | VM_READ

        private readonly int pid;
        private IntPtr readHandle = IntPtr.Zero;
        private IntPtr writeHandle = IntPtr.Zero;

        public ProcessMemory(int targetPid) { pid = targetPid; }
        public int Pid { get { return pid; } }
        public bool CanWrite { get { return writeHandle != IntPtr.Zero; } }

        private IntPtr EnsureRead()
        {
            if (readHandle == IntPtr.Zero) readHandle = OpenProcess(AccessRead, false, pid);
            return readHandle;
        }

        public bool EnsureWrite()
        {
            if (writeHandle != IntPtr.Zero) return true;
            writeHandle = OpenProcess(AccessWrite, false, pid);
            return writeHandle != IntPtr.Zero;
        }

        private static IntPtr ToNative(ulong a) { return new IntPtr(unchecked((long)a)); }

        public bool ReadBytes(ulong addr, byte[] buf, int off, int len)
        {
            if (!Addr.Range(addr, len)) return false;
            IntPtr h = EnsureRead();
            if (h == IntPtr.Zero) return false;
            IntPtr got;
            if (!ReadProcessMemory(h, ToNative(addr), buf, new IntPtr(len), out got)) return false;
            return got.ToInt64() == len;
        }

        public bool WriteBytes(ulong addr, byte[] buf, int off, int len)
        {
            if (!Addr.Range(addr, len)) return false;
            if (!EnsureWrite()) return false;
            IntPtr got;
            if (!WriteProcessMemory(writeHandle, ToNative(addr), buf, new IntPtr(len), out got)) return false;
            return got.ToInt64() == len;
        }

        public void Dispose()
        {
            if (readHandle != IntPtr.Zero) { CloseHandle(readHandle); readHandle = IntPtr.Zero; }
            if (writeHandle != IntPtr.Zero) { CloseHandle(writeHandle); writeHandle = IntPtr.Zero; }
        }
    }

    /// <summary>selftest 用的合成地址空间：线性 byte[]，基址固定，绝不指向真实进程。</summary>
    internal sealed class MockMemory : IMemory
    {
        public const ulong Base = 0x10000000UL;
        private readonly byte[] arena;
        public MockMemory(int size) { arena = new byte[size]; }
        private bool In(ulong a, int len)
        {
            if (a < Base) return false;
            ulong off = a - Base;
            return (off + (ulong)len) <= (ulong)arena.Length;
        }
        public bool ReadBytes(ulong addr, byte[] buf, int off, int len)
        {
            if (!In(addr, len)) return false;
            Buffer.BlockCopy(arena, (int)(addr - Base), buf, off, len);
            return true;
        }
        public bool WriteBytes(ulong addr, byte[] buf, int off, int len)
        {
            if (!In(addr, len)) return false;
            Buffer.BlockCopy(buf, off, arena, (int)(addr - Base), len);
            return true;
        }
    }

    // ---------------------------------------------------------------- 基元读写
    internal static class M
    {
        public static bool ReadPtr(IMemory m, ulong addr, out ulong v)
        {
            v = 0;
            byte[] b = new byte[8];
            if (!m.ReadBytes(addr, b, 0, 8)) return false;
            v = BitConverter.ToUInt64(b, 0);
            return true;
        }

        public static ulong PtrOr0(IMemory m, ulong addr)
        {
            ulong v;
            if (!ReadPtr(m, addr, out v)) return 0;
            return v;
        }

        public static bool ReadI32(IMemory m, ulong addr, out int v)
        {
            v = 0;
            byte[] b = new byte[4];
            if (!m.ReadBytes(addr, b, 0, 4)) return false;
            v = BitConverter.ToInt32(b, 0);
            return true;
        }

        public static bool ReadU64(IMemory m, ulong addr, out ulong v)
        {
            v = 0;
            byte[] b = new byte[8];
            if (!m.ReadBytes(addr, b, 0, 8)) return false;
            v = BitConverter.ToUInt64(b, 0);
            return true;
        }

        public static bool ReadF32(IMemory m, ulong addr, out float v)
        {
            v = 0f;
            byte[] b = new byte[4];
            if (!m.ReadBytes(addr, b, 0, 4)) return false;
            v = BitConverter.ToSingle(b, 0);
            return true;
        }

        public static bool ReadF64(IMemory m, ulong addr, out double v)
        {
            v = 0d;
            byte[] b = new byte[8];
            if (!m.ReadBytes(addr, b, 0, 8)) return false;
            v = BitConverter.ToDouble(b, 0);
            return true;
        }

        public static bool ReadBool(IMemory m, ulong addr, out bool v)
        {
            v = false;
            byte[] b = new byte[1];
            if (!m.ReadBytes(addr, b, 0, 1)) return false;
            if (b[0] != 0 && b[0] != 1) return false;
            v = b[0] == 1;
            return true;
        }

        public static bool ReadCString(IMemory m, ulong addr, int max, out string s)
        {
            s = null;
            if (!Addr.Range(addr, 1)) return false;
            byte[] b = new byte[max];
            int n = 0;
            for (int i = 0; i < max; i += 32)
            {
                int chunk = Math.Min(32, max - i);
                if (!m.ReadBytes(addr + (ulong)i, b, i, chunk)) return false;
                for (int k = 0; k < chunk; k++)
                {
                    if (b[i + k] == 0) { n = i + k; break; }
                }
                if (n > 0 || b[i] == 0) break;
            }
            if (n <= 0) return false;
            s = Encoding.ASCII.GetString(b, 0, n);
            return true;
        }

        public static string ClassName(IMemory m, ulong obj)
        {
            if (!Addr.Valid(obj)) return null;
            ulong klass = PtrOr0(m, obj + (ulong)Off.ObjectKlass);
            if (!Addr.Valid(klass)) return null;
            ulong namePtr = PtrOr0(m, klass + (ulong)Off.KlassName);
            if (!Addr.Valid(namePtr)) return null;
            string name;
            if (!ReadCString(m, namePtr, 96, out name)) return null;
            return name;
        }

        public static string Il2CppString(IMemory m, ulong strObj)
        {
            if (!Addr.Valid(strObj)) return null;
            int len;
            if (!ReadI32(m, strObj + (ulong)Off.StringLength, out len)) return null;
            if (len < 0 || len > 512) return null;
            if (len == 0) return "";
            byte[] b = new byte[len * 2];
            if (!m.ReadBytes(strObj + (ulong)Off.StringData, b, 0, len * 2)) return null;
            return Encoding.Unicode.GetString(b, 0, len * 2);
        }

        public static bool WriteI32(IMemory m, ulong addr, int v)
        {
            byte[] b = BitConverter.GetBytes(v);
            return m.WriteBytes(addr, b, 0, 4);
        }
        public static bool WriteF32(IMemory m, ulong addr, float v)
        {
            byte[] b = BitConverter.GetBytes(v);
            return m.WriteBytes(addr, b, 0, 4);
        }
        public static bool WriteF64(IMemory m, ulong addr, double v)
        {
            byte[] b = BitConverter.GetBytes(v);
            return m.WriteBytes(addr, b, 0, 8);
        }
        public static bool WriteBool(IMemory m, ulong addr, bool v)
        {
            byte[] b = new byte[1];
            b[0] = v ? (byte)1 : (byte)0;
            return m.WriteBytes(addr, b, 0, 1);
        }
    }

    // ---------------------------------------------------------------- 静态链
    internal sealed class Chain
    {
        public ulong GameAssembly;
        public ulong Method;
        public ulong Klass;
        public ulong Rgctx;
        public ulong SingletonKlass;
        public ulong StaticFields;
        public ulong Holder;
        public ulong Save;
        public string HolderClass = "";
        public string SaveClass = "";
    }

    internal static class Resolver
    {
        public static bool Resolve(IMemory m, ulong gameAssembly, out Chain c, out string err)
        {
            c = null;
            err = null;
            if (!Addr.Valid(gameAssembly)) { err = "GameAssembly 基址无效"; return false; }
            Chain r = new Chain();
            r.GameAssembly = gameAssembly;

            r.Method = M.PtrOr0(m, gameAssembly + (ulong)Off.GaMethod);
            if (!Addr.Valid(r.Method)) { err = "方法指针无效"; return false; }
            r.Klass = M.PtrOr0(m, r.Method + (ulong)Off.MethodKlass);
            if (!Addr.Valid(r.Klass)) { err = "类型指针无效"; return false; }
            r.Rgctx = M.PtrOr0(m, r.Klass + (ulong)Off.KlassRgctx);
            if (!Addr.Valid(r.Rgctx)) { err = "rgctx 无效（存档可能尚未初始化）"; return false; }
            r.SingletonKlass = M.PtrOr0(m, r.Rgctx + (ulong)Off.RgctxSingletonKlass);
            if (!Addr.Valid(r.SingletonKlass)) { err = "单例类型无效"; return false; }
            r.StaticFields = M.PtrOr0(m, r.SingletonKlass + (ulong)Off.SingletonStaticFields);
            if (!Addr.Valid(r.StaticFields)) { err = "静态字段无效"; return false; }
            r.Holder = M.PtrOr0(m, r.StaticFields + (ulong)Off.StaticFieldsHolder);
            if (!Addr.Valid(r.Holder)) { err = "DataHolder 尚未创建（游戏未进入主界面）"; return false; }

            string hn = M.ClassName(m, r.Holder);
            r.HolderClass = hn == null ? "(读取失败)" : hn;
            if (hn == null || !string.Equals(hn, "DataHolder", StringComparison.Ordinal))
            {
                err = "类型校验失败：期望 DataHolder，实际 " + r.HolderClass;
                return false;
            }

            r.Save = M.PtrOr0(m, r.Holder + (ulong)Off.HolderSave);
            if (r.Save == 0)
            {
                c = r; // holder 有效但存档未载入
                return true;
            }
            if (!Addr.Valid(r.Save)) { err = "存档对象地址无效"; return false; }
            string sn = M.ClassName(m, r.Save);
            r.SaveClass = sn == null ? "(读取失败)" : sn;
            if (sn == null || !string.Equals(sn, "SaveData", StringComparison.Ordinal))
            {
                err = "类型校验失败：期望 SaveData，实际 " + r.SaveClass;
                return false;
            }
            c = r;
            return true;
        }
    }

    // ---------------------------------------------------------------- 快照
    internal sealed class Snapshot
    {
        public bool Connected;
        public int Pid;
        public bool HashOk;
        public ulong GameAssembly, Holder, Save;
        public string HolderClass = "";
        public string SaveClass = "";
        public bool SaveLoaded;
        public string Error = "";
        public int Gold, Date, Difficulty, BodySize, Preset, Level, Achievements;
        public int NewGamePlus, Arrests, BestClearDays, BestOuting, TutorialLevel, Aroused;
        public double Sperm;
        public float MaxStamina;
        public bool InfinitePlay, BunnyPolice, GroupedInv, OutfitChanged, AchInit, PendingChoice;
        public int FetishMask, SeenGuides, SexNpcMask, ChasedMask, NoiseMask, UndetectedMask, EndingMask;
        public bool MainCharPresent;
        public float CharStamina, CharMaxStamina;
        public int InvCount, QuestCount;
        public List<ItemView> Items = new List<ItemView>();
        public List<QuestView> Quests = new List<QuestView>();
    }

    internal static class Data
    {
        public static void ReadBasics(IMemory m, Chain c, Snapshot s)
        {
            ulong sv = c.Save;
            int i; double d; float f; bool b;
            if (M.ReadI32(m, sv + (ulong)Off.Gold, out i)) s.Gold = i;
            if (M.ReadI32(m, sv + (ulong)Off.CurrentDate, out i)) s.Date = i;
            if (M.ReadI32(m, sv + (ulong)Off.Difficulty, out i)) s.Difficulty = i;
            if (M.ReadI32(m, sv + (ulong)Off.CurrentBodySize, out i)) s.BodySize = i;
            if (M.ReadI32(m, sv + (ulong)Off.SelectedPresetIndex, out i)) s.Preset = i;
            if (M.ReadI32(m, sv + (ulong)Off.CurrentLevel, out i)) s.Level = i;
            if (M.ReadI32(m, sv + (ulong)Off.TotalAchievements, out i)) s.Achievements = i;
            if (M.ReadI32(m, sv + (ulong)Off.NewGamePlusCount, out i)) s.NewGamePlus = i;
            if (M.ReadI32(m, sv + (ulong)Off.TotalArrestCount, out i)) s.Arrests = i;
            if (M.ReadI32(m, sv + (ulong)Off.BestClearDays, out i)) s.BestClearDays = i;
            if (M.ReadI32(m, sv + (ulong)Off.BestOutingEjaculations, out i)) s.BestOuting = i;
            if (M.ReadI32(m, sv + (ulong)Off.LastTutorialGuideLevel, out i)) s.TutorialLevel = i;
            if (M.ReadF64(m, sv + (ulong)Off.CurrentSperm, out d)) s.Sperm = d;
            if (M.ReadF32(m, sv + (ulong)Off.MaxStamina, out f)) s.MaxStamina = f;
            if (M.ReadBool(m, sv + (ulong)Off.InfinitePlay, out b)) s.InfinitePlay = b;
            if (M.ReadBool(m, sv + (ulong)Off.BunnyPoliceUnlocked, out b)) s.BunnyPolice = b;
            if (M.ReadBool(m, sv + (ulong)Off.HasGroupedInventory, out b)) s.GroupedInv = b;
            if (M.ReadBool(m, sv + (ulong)Off.OutfitChanged, out b)) s.OutfitChanged = b;
            if (M.ReadBool(m, sv + (ulong)Off.AchHistoryInit, out b)) s.AchInit = b;
            if (M.ReadBool(m, sv + (ulong)Off.PendingEndingChoice, out b)) s.PendingChoice = b;
            if (M.ReadI32(m, sv + (ulong)Off.FetishMask, out i)) s.FetishMask = i;
            if (M.ReadI32(m, sv + (ulong)Off.SeenScreenGuides, out i)) s.SeenGuides = i;
            if (M.ReadI32(m, sv + (ulong)Off.SexNpcTypeMask, out i)) s.SexNpcMask = i;
            if (M.ReadI32(m, sv + (ulong)Off.MapChasedMask, out i)) s.ChasedMask = i;
            if (M.ReadI32(m, sv + (ulong)Off.MapNoiseAlertMask, out i)) s.NoiseMask = i;
            if (M.ReadI32(m, sv + (ulong)Off.MapUndetectedClearMask, out i)) s.UndetectedMask = i;
            if (M.ReadI32(m, sv + (ulong)Off.ViewedEndingMask, out i)) s.EndingMask = i;

            if (M.ReadI32(m, c.Holder + (ulong)Off.HolderArousedLevel, out i)) s.Aroused = i;
            ulong mc = M.PtrOr0(m, c.Holder + (ulong)Off.HolderMainChar);
            if (Addr.Valid(mc))
            {
                string cn = M.ClassName(m, mc);
                if (cn != null && cn.IndexOf("MainCharStateControl", StringComparison.Ordinal) >= 0)
                {
                    s.MainCharPresent = true;
                    if (M.ReadF32(m, mc + (ulong)Off.MainCharCurrentStamina, out f)) s.CharStamina = f;
                    if (M.ReadF32(m, mc + (ulong)Off.MainCharMaxStamina, out f)) s.CharMaxStamina = f;
                }
            }

            List<ItemView> items;
            string err;
            if (ReadInventory(m, c, out items, out err))
            {
                s.Items = items;
                s.InvCount = items.Count;
            }
            List<QuestView> quests;
            if (ReadQuests(m, c, out quests, out err))
            {
                s.Quests = quests;
                s.QuestCount = quests.Count;
            }
        }

        // ---- 背包 ----
        public static bool ReadInventory(IMemory m, Chain c, out List<ItemView> items, out string err)
        {
            items = new List<ItemView>();
            err = null;
            ulong inv = M.PtrOr0(m, c.Save + (ulong)Off.Inventory);
            if (!Addr.Valid(inv)) { err = "InventoryData 指针无效"; return false; }
            string cn = M.ClassName(m, inv);
            if (cn == null || cn.IndexOf("InventoryData", StringComparison.Ordinal) < 0)
            {
                err = "背包类型校验失败：" + (cn == null ? "(读取失败)" : cn);
                return false;
            }
            ulong list = M.PtrOr0(m, inv + (ulong)Off.InvList);
            if (!Addr.Valid(list)) { err = "List<ItemData> 指针无效"; return false; }
            int size;
            if (!M.ReadI32(m, list + (ulong)Off.ListSize, out size)) { err = "无法读取列表长度"; return false; }
            if (size < 0 || size > Cfg.MaxListItems) { err = "列表长度越界：" + size; return false; }
            if (size == 0) return true;
            ulong arr = M.PtrOr0(m, list + (ulong)Off.ListItems);
            if (!Addr.Valid(arr)) { err = "列表底层数组指针无效"; return false; }
            ulong len;
            if (!M.ReadU64(m, arr + (ulong)Off.ArrayLength, out len)) { err = "无法读取数组长度"; return false; }
            if (len > (ulong)Cfg.MaxListItems) { err = "数组长度越界：" + len; return false; }
            if ((ulong)size > len) { err = "列表长度大于数组长度，结构不一致"; return false; }
            ulong data = arr + (ulong)Off.ArrayData;
            for (int i = 0; i < size; i++)
            {
                ulong p = M.PtrOr0(m, data + (ulong)(i * 8));
                if (!Addr.Valid(p)) continue;
                ItemView it = new ItemView();
                it.Ptr = p;
                it.Index = i;
                int v;
                if (M.ReadI32(m, p + (ulong)Off.ItemDatabaseId, out v)) it.DbId = v;
                if (M.ReadI32(m, p + (ulong)Off.ItemType, out v)) it.ItemType = v;
                if (M.ReadI32(m, p + (ulong)Off.ItemMaxCount, out v)) it.MaxCount = v;
                if (M.ReadI32(m, p + (ulong)Off.ItemX, out v)) it.X = v;
                if (M.ReadI32(m, p + (ulong)Off.ItemY, out v)) it.Y = v;
                if (M.ReadI32(m, p + (ulong)Off.ItemState, out v)) it.State = v;
                if (M.ReadI32(m, p + (ulong)Off.ItemInvType, out v)) it.InvType = v;
                if (M.ReadI32(m, p + (ulong)Off.ItemCount, out v)) it.Count = v;
                bool bb;
                if (M.ReadBool(m, p + (ulong)Off.ItemPurchased, out bb)) it.Purchased = bb;
                string nm = M.Il2CppString(m, M.PtrOr0(m, p + (ulong)Off.ItemRawName));
                it.Name = nm == null ? "" : nm;
                items.Add(it);
            }
            return true;
        }

        // ---- 任务 ----
        public static bool ReadQuests(IMemory m, Chain c, out List<QuestView> quests, out string err)
        {
            quests = new List<QuestView>();
            err = null;
            ulong arr = M.PtrOr0(m, c.Save + (ulong)Off.QuestList);
            if (arr == 0) return true;
            if (!Addr.Valid(arr)) { err = "任务数组指针无效"; return false; }
            ulong len;
            if (!M.ReadU64(m, arr + (ulong)Off.ArrayLength, out len)) { err = "无法读取任务数组长度"; return false; }
            if (len > (ulong)Cfg.MaxQuests) { err = "任务数组长度越界：" + len; return false; }
            ulong data = arr + (ulong)Off.ArrayData;
            for (int i = 0; i < (int)len; i++)
            {
                ulong p = M.PtrOr0(m, data + (ulong)(i * 8));
                if (!Addr.Valid(p)) continue;
                QuestView q = new QuestView();
                q.Ptr = p;
                q.Index = i;
                int v;
                if (M.ReadI32(m, p + (ulong)Off.QuestId, out v)) q.Id = v;
                if (M.ReadI32(m, p + (ulong)Off.QuestLevel, out v)) q.Level = v;
                if (M.ReadI32(m, p + (ulong)Off.QuestMaxCount, out v)) q.MaxCount = v;
                if (M.ReadI32(m, p + (ulong)Off.QuestCurrent, out v)) q.Current = v;
                string nm = M.Il2CppString(m, M.PtrOr0(m, p + (ulong)Off.QuestRawName));
                q.Name = nm == null ? "" : nm;
                quests.Add(q);
            }
            return true;
        }

        // ---- 存档中的既有数组 ----
        public static bool ReadArray(IMemory m, Chain c, int offset, ArrayKind kind, out ulong arr, out int len, out string err)
        {
            arr = 0; len = 0; err = null;
            arr = M.PtrOr0(m, c.Save + (ulong)offset);
            if (arr == 0) { err = "数组为空"; return false; }
            if (!Addr.Valid(arr)) { err = "数组指针无效"; return false; }
            ulong l;
            if (!M.ReadU64(m, arr + (ulong)Off.ArrayLength, out l)) { err = "无法读取数组长度"; return false; }
            if (l > (ulong)Cfg.MaxArrayElems) { err = "数组长度越界：" + l; return false; }
            len = (int)l;
            return true;
        }
    }

    internal enum ArrayKind { I32 = 4, F64 = 8 }

    internal sealed class ItemView
    {
        public ulong Ptr;
        public int Index, DbId, ItemType, MaxCount, X, Y, State, InvType, Count;
        public bool Purchased;
        public string Name = "";
    }

    internal sealed class QuestView
    {
        public ulong Ptr;
        public int Index, Id, Level, MaxCount, Current;
        public string Name = "";
    }

    internal static class Labels
    {
        public static string InvTypeName(int t)
        {
            switch (t)
            {
                case 0: return "已装备槽";
                case 1: return "背包";
                case 2: return "秘密口袋";
                case 3: return "仓库";
                case 4: return "商店";
                case 5: return "发型槽";
                case 6: return "快捷栏1";
                case 7: return "快捷栏2";
                case 8: return "快捷栏3";
                case 9: return "快捷栏4";
                case 10: return "消耗品仓库";
                case 11: return "杂物仓库";
                case 12: return "消耗品商店";
                case 13: return "杂物商店";
                default: return "未知(" + t.ToString(CultureInfo.InvariantCulture) + ")";
            }
        }
        public static string StateName(int s)
        {
            switch (s)
            {
                case 0: return "未装备";
                case 1: return "封印";
                case 2: return "已装备";
                default: return "?" + s.ToString(CultureInfo.InvariantCulture);
            }
        }
        public static string DiffName(int d)
        {
            switch (d) { case 0: return "普通"; case 1: return "简单"; case 2: return "困难"; default: return "未知(" + d + ")"; }
        }
        public static string BodyName(int b)
        {
            switch (b) { case 0: return "无"; case 1: return "小"; case 2: return "中"; case 3: return "大"; case 4: return "特大"; default: return "未知(" + b + ")"; }
        }
        public static string PartName(string p)
        {
            switch (p)
            {
                case "Bra": return "胸罩";
                case "Panty": case "Panties": return "内裤";
                case "Full": return "全身图";
                case "UpperDress": return "上衣";
                case "UnderDress": return "下装";
                case "OuterDress": return "外衣";
                case "Dress": return "连衣裙";
                case "Stocking": case "Stockings": return "长袜";
                case "Pantyhose": return "连裤袜";
                case "KneeSocks": return "及膝袜";
                case "Shoes": case "Shoe": return "鞋子";
                case "Hat": return "帽子";
                case "Hat_01": return "帽子A";
                case "Hat_02": return "帽子B";
                case "Arm": return "手臂";
                case "Glove": case "Gloves": return "手套";
                case "Acc01": return "配饰1";
                case "Acc02": return "配饰2";
                case "HeadAcc01": return "头饰";
                case "HeadAcc_01": return "头饰A";
                case "HeadAcc_02": return "头饰B";
                case "Inner": case "Inner_01": return "内衣";
                case "Inner_02": return "内衣B";
                case "InnerUpper": return "内上衣";
                case "Main": return "主体";
                case "Leotard": return "紧身衣";
                case "Skirt": return "裙子";
                case "Shirt": return "衬衫";
                case "Underwear": return "底裤";
                case "New_Full": return "新版全套";
                default: return p;
            }
        }
    }

    // ---------------------------------------------------------------- 服装目录
    // 提取自游戏 NoviceSuccubusLillia_Data/StreamingAssets/aa/catalog.json 的
    // Image_* 缩略图键（36 系列 / 421 键）。Match 为背包物品名匹配关键词（'|' 分隔），
    // 用于判断该系列是否已被当前存档拥有。
    internal sealed class DressCatalog
    {
        internal sealed class Series
        {
            public string Key, Zh, Match;
            public string[] Colors; public string[] Parts;
            public Series(string key, string zh, string match, string[] colors, string[] parts)
            { Key = key; Zh = zh; Match = match; Colors = colors; Parts = parts; }
        }

        internal static readonly Series[] All = new Series[]
        {
            new Series("DefaultDress","初始服装","Default ",new string[]{"C1"},new string[]{"Glove","Hat","HeadAcc_01","HeadAcc_02","Inner","Main","Shoes","Stockings"}),
            new Series("SummerStreat","夏日街头","Summer Streat",new string[]{"C01","C02"},new string[]{"Acc01","Arm","Bra","Full","Panty","UnderDress","UpperDress"}),
            new Series("Lottie","洛蒂","Lottie",new string[]{"C01"},new string[]{"Acc01","Acc02","Full","Hat","HeadAcc01","Shoes","Stockings","UnderDress","UpperDress"}),
            new Series("SweetEvent","甜蜜活动","Sweet Event",new string[]{"C01","C02"},new string[]{"Acc01","Bra","Full","Hat","Panty","Stockings","UpperDress"}),
            new Series("City_Chic","都市Chic","City Chic",new string[]{"C01"},new string[]{"Acc01","Full","Panty","Shoes","Skirt","Stocking","UpperDress"}),
            new Series("Sport_Swimsuit","运动泳装","Sport Swimsuit",new string[]{"C01","C02"},new string[]{"Bra","Panty"}),
            new Series("AHE","AHE内衣","AHE ",new string[]{"C01","C02"},new string[]{"Bra","Full","Panty"}),
            new Series("AdamEve_Bodysuit","亚当夏娃·紧身衣","AdamEve's Bodysuit",new string[]{"C01","C02","C03","C04"},new string[]{"Stocking"}),
            new Series("AdamEve_FullSuit","亚当夏娃·连体衣","AdamEve's Full-BodySuit",new string[]{"C01","C02","C03"},new string[]{"Arm"}),
            new Series("AdamEve_Gloves","亚当夏娃·手套","AdamEve's Glove",new string[]{"C01","C02","C03","C04"},new string[]{"Arm"}),
            new Series("AdamEve_KneeSocks","亚当夏娃·及膝袜","AdamEve's Knee Socks",new string[]{"C01","C02","C03","C04"},new string[]{"Stocking"}),
            new Series("AdamEve_Leotard","亚当夏娃·紧身裙","AdamEve's Leotard",new string[]{"C01"},new string[]{"Stocking"}),
            new Series("AdamEve_Panty","亚当夏娃·内裤","AdamEve's Panty",new string[]{"C01"},new string[]{"Panty"}),
            new Series("AdamEve_Pantyhose","亚当夏娃·连裤袜","AdamEve's Pantyhose",new string[]{"C01","C02","C03","C04","C05","C06","C07","C08"},new string[]{"Stocking"}),
            new Series("Tenpas_Underwear","天帕斯内衣","Tenpas",new string[]{"C01","C02","C03","C04","C05","C06","C07","C08"},new string[]{"Bra","Panty","Stocking"}),
            new Series("SailorMizu","水手泳装","Sailor Swimsuit",new string[]{"C01","C02","C03"},new string[]{"Full","Hat_01","Hat_02","Inner_01","Inner_02","New_Full","Shirt","Shoes","Stockings"}),
            new Series("Evergrace_Bunny","常青兔女郎","Evergrace Bunny",new string[]{"C01","C02"},new string[]{"Full","Hat","Shoes","Stockings","UnderDress","UpperDress"}),
            new Series("RomanceChina","中华浪漫","Romance China",new string[]{"C01","C02"},new string[]{"Arm","Dress","Full","Inner","Shoes","Stockings"}),
            new Series("Melt","熔融","Melt ",new string[]{"C01","C02"},new string[]{"Full","Inner","InnerUpper","Shoes","Stockings","UnderDress","UpperDress"}),
            new Series("Lunabelle","月铃","Lunabelle",new string[]{"C01"},new string[]{"Bra","Full","Hat","HeadAcc01","Panty","Shoes","Stocking","UpperDress"}),
            new Series("Asmodeus","阿斯莫德","Asmodeus",new string[]{"C01","C02"},new string[]{"Arm","Full","Stocking","UnderDress","UpperDress"}),
            new Series("Asobi_Bikini","游乐比基尼","Asobi",new string[]{"C01","C02","C03"},new string[]{"Bra","Full","Panty","UpperDress"}),
            new Series("Bunny_Police","兔子警察","Police",new string[]{"C01"},new string[]{"Acc02","Arm","Full","Hat","HeadAcc01","Stocking","UnderDress","UpperDress"}),
            new Series("Cold_Blood","冷血","Cold Blood",new string[]{"C01","C02","C03"},new string[]{"Acc01","Acc02","Bra","Full","HeadAcc01","Panty","Shoes","Stockings","UnderDress","UpperDress"}),
            new Series("Cruciform_Seduction","十字诱惑","Cruciform",new string[]{"C01"},new string[]{"Acc01","Bra","Full","Hat","Panty","Stocking","UnderDress","UpperDress"}),
            new Series("Gyakubani","逆绑","Gyakubani",new string[]{"C01"},new string[]{"Acc02","Arm","Bra","Full","Hat","Panty","Shoes","Stocking","UpperDress"}),
            new Series("KistuneOni","狐鬼","Kistune",new string[]{"C01"},new string[]{"Arm","Dress","Full","Hat","HeadAcc_01","Panty","Shoes","Stocking"}),
            new Series("LilithVice","莉莉丝之恶","Lilith",new string[]{"C01"},new string[]{"Acc01","Acc02","Arm","Full","Hat","Leotard","Shoes","Stocking","UnderDress","UpperDress"}),
            new Series("Maid_Bikini","女仆比基尼","Maid",new string[]{"C01"},new string[]{"Acc01","Arm","Bra","Full","Hat","Panty","UpperDress"}),
            new Series("Succubus_Bunny","魅魔兔女郎","Succubus Bunny",new string[]{"C01","C02","C03"},new string[]{"Acc02","Arm","Bra","Full","Hat","HeadAcc01","Shoes","Stocking"}),
            new Series("Succubus_Lingerie","魅魔内衣","Succubus Lingerie",new string[]{"C01","C02","C03"},new string[]{"Acc01","Acc02","Arm","Full","HeadAcc01","Panties","Stockings","UpperDress"}),
            new Series("SummerMiko","夏日巫女","Miko",new string[]{"C01"},new string[]{"Arm","Full","HeadAcc01","OuterDress","Shoes","Stockings","Underwear"}),
            new Series("SweetService","甜蜜服务","Sweet Service",new string[]{"C01","C02"},new string[]{"Acc01","Acc02","Bra","Full","Panty","Stocking","UnderDress","UpperDress"}),
            new Series("SweetSuccubus","甜蜜魅魔","Sweet Succubus",new string[]{"C01"},new string[]{"Acc01","Acc02","Arm","Bra","Full","HeadAcc01","Stocking","UpperDress"}),
            new Series("heat_wave","热浪","Heat",new string[]{"C01","C02"},new string[]{"Bra","Full","Stocking"}),
            new Series("Ustripe","U条纹","Ustripe",new string[]{"C01","C02","C03"},new string[]{"Panty"}),
        };

        /// <summary>统计每个系列在背包（容器 0/1/2/3/5）里拥有的不同物品数。</summary>
        public static int[] OwnedPieces(List<ItemView> items)
        {
            int[] result = new int[All.Length];
            if (items == null) return result;
            for (int si = 0; si < All.Length; si++)
            {
                Series s = All[si];
                if (string.IsNullOrEmpty(s.Match)) continue;
                string[] kws = s.Match.Split('|');
                HashSet<int> ids = new HashSet<int>();
                foreach (ItemView it in items)
                {
                    if (it.InvType != 0 && it.InvType != 1 && it.InvType != 2 && it.InvType != 3 && it.InvType != 5) continue;
                    if (it.Name == null || it.Name.Length == 0) continue;
                    foreach (string kw in kws)
                    {
                        if (kw.Length > 0 && it.Name.IndexOf(kw, StringComparison.Ordinal) >= 0) { ids.Add(it.DbId); break; }
                    }
                }
                result[si] = ids.Count;
            }
            return result;
        }

        public static int OwnedSeriesCount(int[] owned)
        {
            int n = 0;
            for (int i = 0; i < All.Length; i++)
                if (!string.IsNullOrEmpty(All[i].Match) && owned != null && owned[i] > 0) n++;
            return n;
        }

        public static int TrackableSeriesCount()
        {
            int n = 0;
            for (int i = 0; i < All.Length; i++) if (!string.IsNullOrEmpty(All[i].Match)) n++;
            return n;
        }
    }

    // ---------------------------------------------------------------- 控制台
    internal static class Con
    {
        [DllImport("kernel32.dll")]
        private static extern bool AttachConsole(int pid);
        [DllImport("kernel32.dll")]
        private static extern bool AllocConsole();
        [DllImport("kernel32.dll")]
        private static extern IntPtr GetStdHandle(int n);

        public static void EnsureStdout()
        {
            try
            {
                IntPtr h = GetStdHandle(-11);
                if (h == IntPtr.Zero || h == new IntPtr(-1))
                {
                    if (!AttachConsole(-1)) AllocConsole();
                }
                StreamWriter sw = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false));
                sw.AutoFlush = true;
                Console.SetOut(sw);
            }
            catch { }
        }
    }

    // ---------------------------------------------------------------- 会话
    internal sealed class Gen
    {
        public int Pid;
        public long Start;
        public ulong Holder, Save;
        public Gen(int pid, long start, ulong h, ulong s) { Pid = pid; Start = start; Holder = h; Save = s; }
        public bool Same(int pid, long start, ulong h, ulong s)
        {
            return Pid == pid && Start == start && Holder == h && Save == s;
        }
    }

    internal sealed class UndoEntry
    {
        public ulong Addr;
        public byte[] Bytes;
    }

    internal sealed class LockSpec
    {
        public string Name;
        public Func<IMemory, Chain, string> Apply;
    }

    // ================================================================ UI 基础件
    internal static class Theme
    {
        public static readonly Color Back = Color.FromArgb(24, 25, 29);
        public static readonly Color Side = Color.FromArgb(15, 16, 20);
        public static readonly Color Card = Color.FromArgb(34, 36, 42);
        public static readonly Color CardDeep = Color.FromArgb(28, 30, 35);
        public static readonly Color Line = Color.FromArgb(56, 60, 68);
        public static readonly Color Text = Color.FromArgb(235, 236, 240);
        public static readonly Color Sub = Color.FromArgb(150, 155, 163);
        public static readonly Color Accent = Color.FromArgb(232, 121, 168);   // 魅魔粉
        public static readonly Color Amber = Color.FromArgb(245, 166, 76);     // 数值琥珀
        public static readonly Color Ok = Color.FromArgb(108, 199, 132);
        public static readonly Color Bad = Color.FromArgb(230, 105, 110);
        public static readonly Color OkBack = Color.FromArgb(30, 54, 38);
        public static readonly Color BadBack = Color.FromArgb(58, 30, 33);

        public static readonly Font F = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        public static readonly Font Fb = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold, GraphicsUnit.Point);
        public static readonly Font FNav = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Regular, GraphicsUnit.Point);
        public static readonly Font FTitle = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold, GraphicsUnit.Point);
        public static readonly Font FPage = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold, GraphicsUnit.Point);
        public static readonly Font FNum = new Font("Microsoft YaHei UI", 17F, FontStyle.Bold, GraphicsUnit.Point);
        public static readonly Font FLogo = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold, GraphicsUnit.Point);

        public static GraphicsPath Round(Rectangle r, int rad)
        {
            GraphicsPath p = new GraphicsPath();
            if (r.Width <= 0 || r.Height <= 0) { p.AddRectangle(new Rectangle(0, 0, 1, 1)); return p; }
            int d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    /// <summary>圆角卡片面板：标题 + 内容宿主。</summary>
    internal sealed class Card : Panel
    {
        private readonly string title;
        public readonly Panel Body = new Panel();
        public Card(string cardTitle, int w, int h)
        {
            title = cardTitle;
            Size = new Size(w, h);
            BackColor = Theme.Card;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Body.Dock = DockStyle.Fill;
            Body.Padding = new Padding(12, 26, 12, 10);
            Body.BackColor = Color.Transparent;
            Controls.Add(Body);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath p = Theme.Round(new Rectangle(0, 0, Width - 1, Height - 1), 12))
            {
                using (SolidBrush b = new SolidBrush(Theme.Card)) e.Graphics.FillPath(b, p);
                using (Pen pen = new Pen(Theme.Line)) e.Graphics.DrawPath(pen, p);
            }
            if (!string.IsNullOrEmpty(title))
            {
                using (SolidBrush ab = new SolidBrush(Theme.Accent))
                    e.Graphics.FillRectangle(ab, 12, 13, 3, 14);
                TextRenderer.DrawText(e.Graphics, title, Theme.Fb, new Point(22, 10), Theme.Text);
            }
        }
    }

    /// <summary>侧边栏导航按钮。</summary>
    internal sealed class NavButton : Control
    {
        private readonly string glyph;
        private readonly string text;
        public readonly string Key;
        private bool active;
        private bool hover;

        public NavButton(string key, string g, string t)
        {
            Key = key; glyph = g; text = t;
            Height = 40;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }
        public bool Active
        {
            get { return active; }
            set { active = value; Invalidate(); }
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (active)
            {
                using (SolidBrush b = new SolidBrush(Color.FromArgb(43, 40, 52))) g.FillRectangle(b, ClientRectangle);
                using (SolidBrush b = new SolidBrush(Theme.Accent)) g.FillRectangle(b, 0, 6, 3, Height - 12);
            }
            else if (hover)
            {
                using (SolidBrush b = new SolidBrush(Color.FromArgb(30, 31, 38))) g.FillRectangle(b, ClientRectangle);
            }
            Color tc = active ? Theme.Text : Theme.Sub;
            TextRenderer.DrawText(g, glyph, Theme.FNav, new Rectangle(12, 0, 26, Height), Theme.Accent, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
            TextRenderer.DrawText(g, text, active ? Theme.FNav : Theme.FNav, new Rectangle(42, 0, Width - 46, Height), tc, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        }
    }

    /// <summary>状态胶囊标签。</summary>
    internal sealed class Pill : Control
    {
        private string txt = "";
        private Color fore = Theme.Sub;
        private Color back = Color.FromArgb(40, 42, 48);
        public Pill() { SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true); Height = 26; }
        public void Set(string t, Color f, Color b) { txt = t; fore = f; back = b; Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath p = Theme.Round(ClientRectangle, Height / 2))
            {
                using (SolidBrush bb = new SolidBrush(back)) e.Graphics.FillPath(bb, p);
                SizeF ts = e.Graphics.MeasureString(txt, Theme.Fb, Width);
                int dot = txt.Length > 0 ? 8 : 0;
                using (SolidBrush fb = new SolidBrush(fore))
                    e.Graphics.DrawString(txt, Theme.Fb, fb, (Width - (int)ts.Width - dot) / 2 + dot, (Height - (int)ts.Height) / 2);
                if (dot > 0)
                {
                    int dy = (Height - 6) / 2;
                    using (SolidBrush fb = new SolidBrush(fore))
                        e.Graphics.FillEllipse(fb, 12, dy, 6, 6);
                }
            }
        }
    }

    // ================================================================ GUI
    internal sealed class MainForm : Form
    {
        private readonly string gameDir;
        private readonly string baseDir;

        private ProcessMemory mem;
        private int pid;
        private ulong gaBase;
        private bool hashOk;
        private int hashPid;

        private System.Windows.Forms.Timer timer;
        private Snapshot snap = new Snapshot();
        private Gen gen;
        private readonly List<UndoEntry> undo = new List<UndoEntry>();
        private int undoBytes;
        private readonly List<Control> writeControls = new List<Control>();
        private int procStartPid = -1;
        private long procStartTicks;
        private readonly Dictionary<string, LockSpec> locks = new Dictionary<string, LockSpec>();
        private readonly HashSet<string> activeLocks = new HashSet<string>();
        private bool busy;
        private bool closing;

        // ---- 侧边栏 / 页面 ----
        private Panel sidebar;
        private readonly Dictionary<string, NavButton> navButtons = new Dictionary<string, NavButton>();
        private readonly Dictionary<string, Control> pages = new Dictionary<string, Control>();
        private Panel contentHost;
        private Label pageTitle;
        private Label pageSub;
        private Pill statusPill;
        private Button btnRefresh, btnBackup, btnUndo;

        // ---- 概览 ----
        private Label ovGold, ovSperm, ovDate, ovLevel, ovStam, ovInv, ovQuest, ovDress;
        private Label ovGoldSub, ovSpermSub, ovDateSub, ovLevelSub, ovStamSub, ovInvSub, ovQuestSub, ovDressSub;
        private Label ovConn, ovChar;
        // ---- 资源 ----
        private TextBox txtGold, txtSperm, txtDate, txtMaxStam, txtSpermLock, txtDateLock;
        private ComboBox cboDiff, cboBody;
        private Label lblCharStam, lblArousedInfo;
        private CheckBox chkSpermLock, chkStamLock, chkDateLock;
        // ---- 背包 ----
        private ListView lvItems;
        private TextBox txtItemCount;
        private Label lblInvInfo;
        // ---- 任务 ----
        private ListView lvQuests;
        private TextBox txtQuestProgress;
        private Label lblQuestInfo;
        // ---- 服装 ----
        private ListView lvDress;
        private Label lblWearInfo, lblDressInfo;
        // ---- 进阶 ----
        private TextBox txtAchievements, txtNgPlus, txtArrests, txtBestDays, txtBestOuting, txtTutorial;
        private TextBox txtFetish, txtGuides, txtSexMask, txtChased, txtNoise, txtUndetected, txtEnding;
        private TextBox txtAroused, txtArrayIndex, txtArrayValue;
        private ComboBox cboArrayField, cboArrayKind;
        private Label lblArrayInfo, lblEnding, lblFlags;

        public MainForm()
        {
            baseDir = AppDomain.CurrentDomain.BaseDirectory;
            gameDir = baseDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            Text = "莉莉娅中文修改器 - Novice Succubus Lillia";
            ClientSize = new Size(1180, 780);
            MinimumSize = new Size(1040, 680);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Back;
            ForeColor = Theme.Text;
            Font = Theme.F;
            DoubleBuffered = true;
            BuildUi();
        }

        // ------------------------------------------------------------ UI 构造
        private Label Lbl(string t, int w, bool bold)
        {
            Label l = new Label();
            l.Text = t;
            l.Width = w;
            l.Height = 22;
            l.ForeColor = bold ? Theme.Accent : Theme.Text;
            l.Font = bold ? Theme.Fb : Theme.F;
            l.TextAlign = ContentAlignment.MiddleLeft;
            l.AutoSize = false;
            l.BackColor = Color.Transparent;
            return l;
        }

        private TextBox Num(int w, string init)
        {
            TextBox t = new TextBox();
            t.Width = w;
            t.Text = init;
            t.BackColor = Theme.CardDeep;
            t.ForeColor = Theme.Text;
            t.BorderStyle = BorderStyle.FixedSingle;
            t.Font = Theme.F;
            return t;
        }

        private Button Btn(string text, int w, EventHandler h)
        {
            Button b = new Button();
            b.Text = text;
            b.Width = w;
            b.Height = 28;
            b.FlatStyle = FlatStyle.Flat;
            b.BackColor = Theme.CardDeep;
            b.ForeColor = Theme.Text;
            b.FlatAppearance.BorderColor = Theme.Line;
            b.FlatAppearance.MouseOverBackColor = Color.FromArgb(48, 50, 58);
            b.Font = Theme.F;
            b.Cursor = Cursors.Hand;
            b.Click += h;
            return b;
        }

        private Button BtnAccent(string text, int w, EventHandler h)
        {
            Button b = Btn(text, w, h);
            b.BackColor = Color.FromArgb(96, 44, 68);
            b.FlatAppearance.BorderColor = Theme.Accent;
            b.ForeColor = Color.FromArgb(255, 224, 238);
            b.FlatAppearance.MouseOverBackColor = Color.FromArgb(122, 58, 88);
            return b;
        }

        private CheckBox Chk(string text, int w, EventHandler h)
        {
            CheckBox c = new CheckBox();
            c.Text = text;
            c.Width = w;
            c.Height = 24;
            c.ForeColor = Theme.Text;
            c.FlatStyle = FlatStyle.Flat;
            c.Font = Theme.F;
            c.BackColor = Color.Transparent;
            c.CheckedChanged += h;
            return c;
        }

        private ComboBox Cbo(int w, string[] items, int sel)
        {
            ComboBox c = new ComboBox();
            c.DropDownStyle = ComboBoxStyle.DropDownList;
            c.Width = w;
            c.BackColor = Theme.CardDeep;
            c.ForeColor = Theme.Text;
            c.FlatStyle = FlatStyle.Flat;
            c.Font = Theme.F;
            c.Items.AddRange(items);
            if (sel >= 0 && sel < items.Length) c.SelectedIndex = sel;
            return c;
        }

        private Panel Row(int h, params Control[] items)
        {
            Panel p = new Panel();
            p.Height = h;
            p.Width = 980;
            p.Margin = new Padding(0, 0, 0, 2);
            p.BackColor = Color.Transparent;
            int x = 0;
            foreach (Control c in items)
            {
                c.Left = x;
                c.Top = Math.Max(0, (h - c.Height) / 2);
                p.Controls.Add(c);
                x += c.Width + 10;
            }
            return p;
        }

        private Label CardLabel(string t, int w)
        {
            Label l = new Label();
            l.Text = t;
            l.Width = w;
            l.Height = 20;
            l.ForeColor = Theme.Sub;
            l.Font = Theme.F;
            l.BackColor = Color.Transparent;
            return l;
        }

        private void BuildUi()
        {
            // ============ 侧边栏 ============
            sidebar = new Panel();
            sidebar.Dock = DockStyle.Left;
            sidebar.Width = 196;
            sidebar.BackColor = Theme.Side;
            sidebar.Padding = new Padding(0, 18, 0, 12);

            Label logo = new Label();
            logo.Text = "リリア修改器";
            logo.Font = Theme.FLogo;
            logo.ForeColor = Theme.Accent;
            logo.AutoSize = true;
            logo.Location = new Point(18, 12);
            sidebar.Controls.Add(logo);

            Label ver = new Label();
            ver.Text = "Novice Succubus Lillia · v2";
            ver.ForeColor = Color.FromArgb(96, 100, 110);
            ver.Font = Theme.F;
            ver.AutoSize = true;
            ver.Location = new Point(20, 40);
            sidebar.Controls.Add(ver);

            Panel sep = new Panel();
            sep.Bounds = new Rectangle(14, 68, sidebar.Width - 28, 1);
            sep.BackColor = Color.FromArgb(38, 40, 47);
            sidebar.Controls.Add(sep);

            AddNav("overview", "◉", "概览", 82);
            AddNav("resource", "◆", "资源与锁定", 126);
            AddNav("inventory", "▤", "背包", 168);
            AddNav("quest", "✎", "任务", 210);
            AddNav("wardrobe", "✿", "服装图鉴", 252);
            AddNav("advanced", "⚙", "进阶数据", 294);

            Label tip = new Label();
            tip.Text = "修改仅写入内存，\n请在游戏内手动保存。\n断开连接后锁定自动停止。";
            tip.ForeColor = Color.FromArgb(96, 100, 110);
            tip.Font = Theme.F;
            tip.Width = 170;
            tip.Height = 70;
            tip.Location = new Point(16, sidebar.Height - 88);
            tip.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            sidebar.Controls.Add(tip);
            Controls.Add(sidebar);

            // ============ 顶部标题条 ============
            Panel header = new Panel();
            header.Dock = DockStyle.Top;
            header.Height = 66;
            header.BackColor = Theme.Back;
            header.Padding = new Padding(24, 0, 20, 0);

            pageTitle = new Label();
            pageTitle.Text = "概览";
            pageTitle.Font = Theme.FPage;
            pageTitle.ForeColor = Theme.Text;
            pageTitle.AutoSize = true;
            pageTitle.Location = new Point(26, 12);
            header.Controls.Add(pageTitle);

            pageSub = new Label();
            pageSub.Text = "实时内存只读轮询 · 点击应用才写入";
            pageSub.ForeColor = Theme.Sub;
            pageSub.Font = Theme.F;
            pageSub.AutoSize = true;
            pageSub.Location = new Point(28, 40);
            header.Controls.Add(pageSub);

            statusPill = new Pill();
            statusPill.Size = new Size(264, 28);
            statusPill.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            statusPill.Location = new Point(header.Width - 524, 19);
            statusPill.Set("● 未连接", Theme.Bad, Theme.BadBack);
            header.Controls.Add(statusPill);

            btnRefresh = Btn("立即刷新", 88, delegate(object s, EventArgs e) { Tick(null, null); });
            btnRefresh.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnRefresh.Location = new Point(header.Width - 240, 19);
            header.Controls.Add(btnRefresh);
            btnBackup = Btn("备份存档", 88, delegate(object s, EventArgs e) { DoBackup(); });
            btnBackup.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnBackup.Location = new Point(header.Width - 144, 19);
            header.Controls.Add(btnBackup);
            btnUndo = Btn("撤销", 88, delegate(object s, EventArgs e) { DoUndo(); });
            btnUndo.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnUndo.Location = new Point(header.Width - 48, 19);
            header.Controls.Add(btnUndo);

            Panel headerLine = new Panel();
            headerLine.Dock = DockStyle.Bottom;
            headerLine.Height = 1;
            headerLine.BackColor = Color.FromArgb(38, 40, 47);
            header.Controls.Add(headerLine);
            header.Controls.SetChildIndex(headerLine, 0);
            Controls.Add(header);

            // ============ 内容区 ============
            contentHost = new Panel();
            contentHost.Dock = DockStyle.Fill;
            contentHost.BackColor = Theme.Back;
            contentHost.Padding = new Padding(24, 14, 24, 14);
            contentHost.AutoScroll = true;
            Controls.Add(contentHost);
            contentHost.BringToFront();

            Panel overview = BuildOverviewPage();
            Panel resource = BuildResourcePage();
            Panel inventory = BuildInventoryPage();
            Panel quest = BuildQuestPage();
            Panel wardrobe = BuildWardrobePage();
            Panel advanced = BuildAdvancedPage();
            pages["overview"] = overview;
            pages["resource"] = resource;
            pages["inventory"] = inventory;
            pages["quest"] = quest;
            pages["wardrobe"] = wardrobe;
            pages["advanced"] = advanced;
            foreach (KeyValuePair<string, Control> kv in pages)
            {
                kv.Value.Dock = DockStyle.Fill;
                contentHost.Controls.Add(kv.Value);
            }
            ShowPage("overview");

            BuildLocks();
            CollectWriteControls(this);
            SetWritesEnabled(false);
        }

        private void CollectWriteControls(Control root)
        {
            foreach (Control c in root.Controls)
            {
                if (c == btnRefresh || c == btnBackup || c == btnUndo) { CollectWriteControls(c); continue; }
                if (c is NavButton || c is Card || c is Pill) { CollectWriteControls(c); continue; }
                Button b = c as Button;
                if (b != null && b.Text != null && b.Text.IndexOf("刷新", StringComparison.Ordinal) >= 0)
                { CollectWriteControls(c); continue; }
                if (b != null || c is CheckBox || c is TextBox || c is ComboBox)
                    writeControls.Add(c);
                CollectWriteControls(c);
            }
        }

        private void AddNav(string key, string glyph, string text, int y)
        {
            NavButton nb = new NavButton(key, glyph, text);
            nb.Bounds = new Rectangle(8, y, sidebar.Width - 16, 40);
            nb.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            nb.Click += delegate(object s, EventArgs e) { ShowPage(key); };
            sidebar.Controls.Add(nb);
            navButtons[key] = nb;
        }

        private void ShowPage(string key)
        {
            foreach (KeyValuePair<string, NavButton> kv in navButtons) kv.Value.Active = kv.Key == key;
            foreach (KeyValuePair<string, Control> kv in pages) kv.Value.Visible = kv.Key == key;
            string t = "概览"; string s = "实时内存只读轮询 · 点击应用才写入";
            if (key == "resource") { t = "资源与锁定"; s = "金币 / 精液储量 / 日期 / 难度 / 体型 / 耐力与三项持续锁定"; }
            if (key == "inventory") { t = "背包"; s = "查看与修改既有物品 · 数量 / 补满 / 移动容器"; }
            if (key == "quest") { t = "任务"; s = "查看与写满既有任务进度，奖励仍需回游戏提交"; }
            if (key == "wardrobe") { t = "服装图鉴"; s = "36 系列服装拥有状态对比 · 未解锁清单 · 当前穿搭与发型"; }
            if (key == "advanced") { t = "进阶数据"; s = "成就 / 掩码 / 开关 / 统计数组（按字段+下标）"; }
            pageTitle.Text = t;
            pageSub.Text = s;
        }

        public void ShowPagePublic(string key) { ShowPage(key); }

        // -------------------------------------------------- 概览页
        private Panel BuildOverviewPage()
        {
            Panel page = new Panel();
            page.BackColor = Theme.Back;
            page.AutoScroll = true;

            Panel flow = new Panel();
            flow.Dock = DockStyle.Fill;
            flow.BackColor = Color.Transparent;

            // 统计卡片 4×2
            ovGold = StatCard(flow, 20, 16, "金币", "—");
            ovSperm = StatCard(flow, 288, 16, "精液储量", "—");
            ovDate = StatCard(flow, 556, 16, "当前天数", "—");
            ovLevel = StatCard(flow, 824, 16, "当前等级", "—");
            ovGoldSub = SubOf(flow, 20, 82); ovSpermSub = SubOf(flow, 288, 82); ovDateSub = SubOf(flow, 556, 82); ovLevelSub = SubOf(flow, 824, 82);
            ovStam = StatCard(flow, 20, 108, "地图耐力", "—");
            ovInv = StatCard(flow, 288, 108, "背包条目", "—");
            ovQuest = StatCard(flow, 556, 108, "任务记录", "—");
            ovDress = StatCard(flow, 824, 108, "服装解锁", "—");
            ovStamSub = SubOf(flow, 20, 174); ovInvSub = SubOf(flow, 288, 174); ovQuestSub = SubOf(flow, 556, 174); ovDressSub = SubOf(flow, 824, 174);

            // 连接信息卡
            Card cConn = new Card("连接与角色", 518, 130);
            cConn.Location = new Point(20, 200);
            ovConn = CardLabel("", 480); ovConn.Location = new Point(12, 6);
            ovChar = CardLabel("", 480); ovChar.Location = new Point(12, 32);
            Label hint = CardLabel("先运行游戏并载入存档，修改器每 0.5 秒自动刷新；\n各项写功能在载入存档后才会启用。", 480);
            hint.Location = new Point(12, 62); hint.Height = 40;
            cConn.Body.Controls.Add(ovConn);
            cConn.Body.Controls.Add(ovChar);
            cConn.Body.Controls.Add(hint);
            flow.Controls.Add(cConn);

            // 当前穿搭卡
            Card cWear = new Card("当前穿搭（只读）", 518, 130);
            cWear.Location = new Point(556, 200);
            lblWearInfo = CardLabel("—", 480);
            lblWearInfo.Location = new Point(12, 8);
            lblWearInfo.Height = 80;
            cWear.Body.Controls.Add(lblWearInfo);
            flow.Controls.Add(cWear);

            page.Controls.Add(flow);
            return page;
        }

        private static Label StatCard(Panel parent, int x, int y, string caption, string value)
        {
            Card c = new Card(null, 248, 86);
            c.Location = new Point(x, y);
            Label cap = new Label();
            cap.Text = caption;
            cap.Font = Theme.F;
            cap.ForeColor = Theme.Sub;
            cap.AutoSize = true;
            cap.Location = new Point(16, 12);
            Label val = new Label();
            val.Text = value;
            val.Font = Theme.FNum;
            val.ForeColor = Theme.Amber;
            val.AutoSize = true;
            val.Location = new Point(16, 34);
            c.Body.Controls.Add(cap);
            c.Body.Controls.Add(val);
            parent.Controls.Add(c);
            return val;
        }

        private static Label SubOf(Panel parent, int x, int y)
        {
            Label l = new Label();
            l.Text = "";
            l.Font = Theme.F;
            l.ForeColor = Theme.Sub;
            l.BackColor = Color.Transparent;
            l.Location = new Point(x + 16, y);
            l.Size = new Size(220, 18);
            parent.Controls.Add(l);
            return l;
        }

        // -------------------------------------------------- 资源页
        private Panel BuildResourcePage()
        {
            Panel page = new Panel();
            page.BackColor = Theme.Back;
            page.AutoScroll = true;

            Card c1 = new Card("基础数值", 500, 268);
            FlowLayoutPanel f1 = new FlowLayoutPanel();
            f1.Dock = DockStyle.Fill;
            f1.FlowDirection = FlowDirection.TopDown;
            f1.WrapContents = false;
            f1.BackColor = Color.Transparent;
            f1.Padding = new Padding(4, 4, 0, 0);

            txtGold = Num(150, "0");
            f1.Controls.Add(Row(30, Lbl("金币", 130, false), txtGold,
                Btn("应用", 70, delegate(object s, EventArgs e) { ApplyGold(); }),
                BtnAccent("一键 9999999", 108, delegate(object s, EventArgs e) { txtGold.Text = "9999999"; ApplyGold(); })));
            txtSperm = Num(150, "0");
            f1.Controls.Add(Row(30, Lbl("精液储量", 130, false), txtSperm,
                Btn("应用", 70, delegate(object s, EventArgs e) { ApplySperm(); })));
            txtDate = Num(150, "1");
            f1.Controls.Add(Row(30, Lbl("当前日期 (天)", 130, false), txtDate,
                Btn("应用", 70, delegate(object s, EventArgs e) { ApplyDate(); })));
            cboDiff = Cbo(150, new string[] { "普通 (Normal)", "简单 (Easy)", "困难 (Hard)" }, 1);
            f1.Controls.Add(Row(30, Lbl("难度", 130, false), cboDiff,
                Btn("应用", 70, delegate(object s, EventArgs e) { ApplyDifficulty(); })));
            cboBody = Cbo(150, new string[] { "无", "小", "中", "大", "特大" }, 0);
            f1.Controls.Add(Row(30, Lbl("体型", 130, false), cboBody,
                Btn("应用", 70, delegate(object s, EventArgs e) { ApplyBodySize(); })));
            txtMaxStam = Num(150, "100");
            f1.Controls.Add(Row(30, Lbl("最大耐力", 130, false), txtMaxStam,
                Btn("应用", 70, delegate(object s, EventArgs e) { ApplyMaxStamina(); })));
            c1.Body.Controls.Add(f1);
            c1.Location = new Point(20, 16);
            page.Controls.Add(c1);

            Card c2 = new Card("状态与持续锁定", 500, 268);
            FlowLayoutPanel f2 = new FlowLayoutPanel();
            f2.Dock = DockStyle.Fill;
            f2.FlowDirection = FlowDirection.TopDown;
            f2.WrapContents = false;
            f2.BackColor = Color.Transparent;
            f2.Padding = new Padding(4, 4, 0, 0);
            lblCharStam = CardLabel("—", 460);
            f2.Controls.Add(Row(24, lblCharStam));
            lblArousedInfo = CardLabel("—", 460);
            f2.Controls.Add(Row(24, lblArousedInfo));

            txtSpermLock = Num(110, "100");
            chkSpermLock = Chk("资源锁（持续写回）", 190, delegate(object s, EventArgs e) { ToggleLock("sperm", chkSpermLock.Checked); });
            f2.Controls.Add(Row(28, chkSpermLock, txtSpermLock));
            chkStamLock = Chk("全耐力锁（需角色在地图中）", 240, delegate(object s, EventArgs e) { ToggleLock("stamina", chkStamLock.Checked); });
            f2.Controls.Add(Row(28, chkStamLock));
            txtDateLock = Num(110, "1");
            chkDateLock = Chk("无限天数（锁日期）", 190, delegate(object s, EventArgs e) { ToggleLock("date", chkDateLock.Checked); });
            f2.Controls.Add(Row(28, chkDateLock, txtDateLock));
            Label note = CardLabel("锁定按 0.5s 周期持续写回；切换存档 / 断开连接自动停止。", 460);
            f2.Controls.Add(Row(24, note));
            c2.Body.Controls.Add(f2);
            c2.Location = new Point(540, 16);
            page.Controls.Add(c2);
            return page;
        }

        // -------------------------------------------------- 背包页
        private Panel BuildInventoryPage()
        {
            Panel page = new Panel();
            page.BackColor = Theme.Back;

            lvItems = new ListView();
            lvItems.Dock = DockStyle.Fill;
            lvItems.View = View.Details;
            lvItems.FullRowSelect = true;
            lvItems.GridLines = false;
            lvItems.MultiSelect = false;
            lvItems.BackColor = Theme.Card;
            lvItems.ForeColor = Theme.Text;
            lvItems.Font = Theme.F;
            lvItems.Columns.Add("名称", 230);
            lvItems.Columns.Add("ID", 60);
            lvItems.Columns.Add("物品类型", 70);
            lvItems.Columns.Add("所在容器", 100);
            lvItems.Columns.Add("数量", 60);
            lvItems.Columns.Add("上限", 60);
            lvItems.Columns.Add("状态", 90);
            lvItems.Columns.Add("坐标", 80);

            Panel bottom = new Panel();
            bottom.Dock = DockStyle.Bottom;
            bottom.Height = 76;
            bottom.BackColor = Theme.Back;

            txtItemCount = Num(100, "0");
            FlowLayoutPanel fb = new FlowLayoutPanel();
            fb.Dock = DockStyle.Fill;
            fb.FlowDirection = FlowDirection.TopDown;
            fb.WrapContents = false;
            fb.BackColor = Color.Transparent;
            fb.Controls.Add(Row(30,
                Lbl("数量", 44, false), txtItemCount,
                Btn("应用到选中", 92, delegate(object s, EventArgs e) { ApplyItemCount(); }),
                Btn("补满选中", 84, delegate(object s, EventArgs e) { RefillSelected(); }),
                Btn("补满全部消耗品", 116, delegate(object s, EventArgs e) { RefillConsumables(); }),
                Btn("移到背包", 84, delegate(object s, EventArgs e) { MoveSelectedContainer(1); }),
                Btn("移到仓库", 84, delegate(object s, EventArgs e) { MoveSelectedContainer(3); }),
                Btn("刷新列表", 84, delegate(object s, EventArgs e) { RefreshInventory(true); })));
            lblInvInfo = CardLabel("", 900);
            fb.Controls.Add(Row(22, lblInvInfo));
            bottom.Controls.Add(fb);

            page.Controls.Add(lvItems);
            page.Controls.Add(bottom);
            lvItems.BringToFront();
            return page;
        }

        // -------------------------------------------------- 任务页
        private Panel BuildQuestPage()
        {
            Panel page = new Panel();
            page.BackColor = Theme.Back;

            lvQuests = new ListView();
            lvQuests.Dock = DockStyle.Fill;
            lvQuests.View = View.Details;
            lvQuests.FullRowSelect = true;
            lvQuests.MultiSelect = false;
            lvQuests.BackColor = Theme.Card;
            lvQuests.ForeColor = Theme.Text;
            lvQuests.Font = Theme.F;
            lvQuests.Columns.Add("任务名称", 340);
            lvQuests.Columns.Add("ID", 70);
            lvQuests.Columns.Add("等级", 60);
            lvQuests.Columns.Add("当前进度", 90);
            lvQuests.Columns.Add("需求数量", 90);

            Panel bottom = new Panel();
            bottom.Dock = DockStyle.Bottom;
            bottom.Height = 76;
            bottom.BackColor = Theme.Back;
            txtQuestProgress = Num(100, "0");
            FlowLayoutPanel fb = new FlowLayoutPanel();
            fb.Dock = DockStyle.Fill;
            fb.FlowDirection = FlowDirection.TopDown;
            fb.WrapContents = false;
            fb.BackColor = Color.Transparent;
            fb.Controls.Add(Row(30,
                Lbl("进度", 44, false), txtQuestProgress,
                Btn("应用到选中", 92, delegate(object s, EventArgs e) { ApplyQuestProgress(); }),
                Btn("完成选中", 84, delegate(object s, EventArgs e) { CompleteQuestSelected(); }),
                Btn("刷新列表", 84, delegate(object s, EventArgs e) { RefreshQuests(true); })));
            lblQuestInfo = CardLabel("进度写满只代表计数完成，奖励仍需回游戏正常提交领取。", 900);
            fb.Controls.Add(Row(22, lblQuestInfo));
            bottom.Controls.Add(fb);

            page.Controls.Add(lvQuests);
            page.Controls.Add(bottom);
            lvQuests.BringToFront();
            return page;
        }

        // -------------------------------------------------- 服装页
        private Panel BuildWardrobePage()
        {
            Panel page = new Panel();
            page.BackColor = Theme.Back;

            lvDress = new ListView();
            lvDress.Dock = DockStyle.Fill;
            lvDress.View = View.Details;
            lvDress.FullRowSelect = true;
            lvDress.MultiSelect = false;
            lvDress.BackColor = Theme.Card;
            lvDress.ForeColor = Theme.Text;
            lvDress.Font = Theme.F;
            lvDress.Columns.Add("系列", 150);
            lvDress.Columns.Add("中文名", 120);
            lvDress.Columns.Add("配色", 90);
            lvDress.Columns.Add("部件", 300);
            lvDress.Columns.Add("拥有状态", 90);
            lvDress.Columns.Add("已有件数", 80);
            lvDress.Columns.Add("获取提示", 250);

            Panel bottom = new Panel();
            bottom.Dock = DockStyle.Bottom;
            bottom.Height = 118;
            bottom.BackColor = Theme.Back;
            FlowLayoutPanel fb = new FlowLayoutPanel();
            fb.Dock = DockStyle.Fill;
            fb.FlowDirection = FlowDirection.TopDown;
            fb.WrapContents = false;
            fb.BackColor = Color.Transparent;
            lblDressInfo = CardLabel("", 1000);
            lblDressInfo.Height = 66;
            fb.Controls.Add(Row(60, lblDressInfo));
            Label note = CardLabel("图鉴共 36 系列（提取自游戏资源目录）。未解锁服装可通过商店购买、地图掉落或活动事件获得；", 1000);
            fb.Controls.Add(Row(20, note));
            Label note2 = CardLabel("地图掉落表（generalDressDropItemIndex / specialDressDropItemIndex）在游戏运行数据中按需加载，本页仅标注拥有状态。", 1000);
            fb.Controls.Add(Row(20, note2));
            bottom.Controls.Add(fb);

            page.Controls.Add(lvDress);
            page.Controls.Add(bottom);
            lvDress.BringToFront();
            return page;
        }

        // -------------------------------------------------- 进阶页
        private Panel BuildAdvancedPage()
        {
            Panel page = new Panel();
            page.BackColor = Theme.Back;
            page.AutoScroll = true;

            Panel flow = new Panel();
            flow.Dock = DockStyle.Fill;
            flow.BackColor = Color.Transparent;

            Card c1 = new Card("存档计数与标记", 1024, 244);
            FlowLayoutPanel f1 = new FlowLayoutPanel();
            f1.Dock = DockStyle.Fill; f1.FlowDirection = FlowDirection.TopDown; f1.WrapContents = false;
            f1.BackColor = Color.Transparent; f1.Padding = new Padding(4, 2, 0, 0);
            txtAchievements = Num(110, "0");
            f1.Controls.Add(Row(30, Lbl("成就总数", 130, false), txtAchievements,
                Btn("应用", 70, delegate(object s, EventArgs e) { ApplyI32("成就总数", Off.TotalAchievements, txtAchievements, 0, 1000000); })));
            txtNgPlus = Num(110, "0");
            f1.Controls.Add(Row(30, Lbl("重复游玩次数", 130, false), txtNgPlus,
                Btn("应用", 70, delegate(object s, EventArgs e) { ApplyI32("重复游玩次数", Off.NewGamePlusCount, txtNgPlus, 0, 1000); })));
            txtTutorial = Num(110, "0");
            f1.Controls.Add(Row(30, Lbl("新手引导进度", 130, false), txtTutorial,
                Btn("应用", 70, delegate(object s, EventArgs e) { ApplyI32("新手引导进度", Off.LastTutorialGuideLevel, txtTutorial, 0, 100000); })));
            txtArrests = Num(110, "0");
            f1.Controls.Add(Row(30, Lbl("累计被逮捕次数", 130, false), txtArrests,
                Btn("应用", 70, delegate(object s, EventArgs e) { ApplyI32("累计被逮捕次数", Off.TotalArrestCount, txtArrests, 0, 1000000); })));
            txtBestDays = Num(110, "0");
            f1.Controls.Add(Row(30, Lbl("最佳通关天数", 130, false), txtBestDays,
                Btn("应用", 70, delegate(object s, EventArgs e) { ApplyI32("最佳通关天数", Off.BestClearDays, txtBestDays, 0, 1000000); })));
            txtBestOuting = Num(110, "0");
            f1.Controls.Add(Row(30, Lbl("最佳外出次数", 130, false), txtBestOuting,
                Btn("应用", 70, delegate(object s, EventArgs e) { ApplyI32("最佳外出次数", Off.BestOutingEjaculations, txtBestOuting, 0, 1000000); })));
            c1.Body.Controls.Add(f1);
            c1.Location = new Point(20, 14);
            flow.Controls.Add(c1);

            Card c2 = new Card("位掩码（十六进制，按原值精确写入）", 1024, 250);
            FlowLayoutPanel f2 = new FlowLayoutPanel();
            f2.Dock = DockStyle.Fill; f2.FlowDirection = FlowDirection.TopDown; f2.WrapContents = false;
            f2.BackColor = Color.Transparent; f2.Padding = new Padding(4, 2, 0, 0);
            txtFetish = Num(140, "0");
            f2.Controls.Add(Row(28, Lbl("性癖对话掩码", 140, false), txtFetish,
                Btn("应用", 70, delegate(object s, EventArgs e) { ApplyMask("性癖对话掩码", Off.FetishMask, txtFetish); })));
            txtGuides = Num(140, "0");
            f2.Controls.Add(Row(28, Lbl("已见界面引导掩码", 140, false), txtGuides,
                Btn("应用", 70, delegate(object s, EventArgs e) { ApplyMask("已见界面引导掩码", Off.SeenScreenGuides, txtGuides); })));
            txtSexMask = Num(140, "0");
            f2.Controls.Add(Row(28, Lbl("性行为对象掩码", 140, false), txtSexMask,
                Btn("应用", 70, delegate(object s, EventArgs e) { ApplyMask("性行为对象掩码", Off.SexNpcTypeMask, txtSexMask); })));
            txtChased = Num(140, "0");
            f2.Controls.Add(Row(28, Lbl("被追捕记录掩码", 140, false), txtChased,
                Btn("应用", 70, delegate(object s, EventArgs e) { ApplyMask("被追捕记录掩码", Off.MapChasedMask, txtChased); })));
            txtNoise = Num(140, "0");
            f2.Controls.Add(Row(28, Lbl("噪音警戒掩码", 140, false), txtNoise,
                Btn("应用", 70, delegate(object s, EventArgs e) { ApplyMask("噪音警戒掩码", Off.MapNoiseAlertMask, txtNoise); })));
            txtUndetected = Num(140, "0");
            f2.Controls.Add(Row(28, Lbl("未被发现通关掩码", 140, false), txtUndetected,
                Btn("应用", 70, delegate(object s, EventArgs e) { ApplyMask("未被发现通关掩码", Off.MapUndetectedClearMask, txtUndetected); })));
            txtEnding = Num(140, "0");
            f2.Controls.Add(Row(28, Lbl("已看结局掩码", 140, false), txtEnding,
                Btn("应用", 70, delegate(object s, EventArgs e) { ApplyMask("已看结局掩码", Off.ViewedEndingMask, txtEnding); })));
            c2.Body.Controls.Add(f2);
            c2.Location = new Point(20, 270);
            flow.Controls.Add(c2);

            Card c3 = new Card("开关式选项", 1024, 128);
            FlowLayoutPanel f3 = new FlowLayoutPanel();
            f3.Dock = DockStyle.Fill; f3.FlowDirection = FlowDirection.TopDown; f3.WrapContents = false;
            f3.BackColor = Color.Transparent; f3.Padding = new Padding(4, 2, 0, 0);
            f3.Controls.Add(Row(28, Chk("无限游戏（infinitePlay）", 260, delegate(object s, EventArgs e) { ApplyBoolToggle("无限游戏", Off.InfinitePlay, ((CheckBox)s).Checked); })));
            f3.Controls.Add(Row(28, Chk("兔子警察已解锁", 260, delegate(object s, EventArgs e) { ApplyBoolToggle("兔子警察已解锁", Off.BunnyPoliceUnlocked, ((CheckBox)s).Checked); })));
            lblFlags = CardLabel("", 960);
            f3.Controls.Add(Row(24, lblFlags));
            c3.Body.Controls.Add(f3);
            c3.Location = new Point(20, 532);
            flow.Controls.Add(c3);

            Card c4 = new Card("兴奋度（DataHolder，高级）", 500, 96);
            FlowLayoutPanel f4 = new FlowLayoutPanel();
            f4.Dock = DockStyle.Fill; f4.FlowDirection = FlowDirection.TopDown; f4.WrapContents = false;
            f4.BackColor = Color.Transparent; f4.Padding = new Padding(4, 2, 0, 0);
            txtAroused = Num(110, "0");
            f4.Controls.Add(Row(30, Lbl("兴奋度等级", 120, false), txtAroused,
                Btn("应用", 70, delegate(object s, EventArgs e) { ApplyAroused(); })));
            c4.Body.Controls.Add(f4);
            c4.Location = new Point(20, 672);
            flow.Controls.Add(c4);

            Card c5 = new Card("既有统计数组（按字段+下标，仅改既有元素）", 1024, 170);
            FlowLayoutPanel f5 = new FlowLayoutPanel();
            f5.Dock = DockStyle.Fill; f5.FlowDirection = FlowDirection.TopDown; f5.WrapContents = false;
            f5.BackColor = Color.Transparent; f5.Padding = new Padding(4, 2, 0, 0);
            cboArrayField = Cbo(230, new string[] {
                "MapClearCount +0xD0",
                "totalSexCount +0xA0",
                "tempSexCount +0xA8",
                "totalSexCountByPosition +0xB0",
                "tempSexCountByPosition +0xB8",
                "totalSpermByPosition +0xC0",
                "tempSpermByPosition +0xC8" }, 0);
            cboArrayKind = Cbo(110, new string[] { "int32", "double" }, 0);
            cboArrayField.SelectedIndexChanged += delegate(object s, EventArgs e) { AutoKind(); };
            f5.Controls.Add(Row(30, Lbl("字段", 54, false), cboArrayField, Lbl("元素类型", 66, false), cboArrayKind,
                Btn("读取", 70, delegate(object s, EventArgs e) { ReadArrayValue(); })));
            txtArrayIndex = Num(80, "0");
            txtArrayValue = Num(140, "0");
            f5.Controls.Add(Row(30, Lbl("下标", 54, false), txtArrayIndex, Lbl("数值", 54, false), txtArrayValue,
                Btn("应用", 70, delegate(object s, EventArgs e) { ApplyArrayValue(); }),
                Btn("刷新字段信息", 104, delegate(object s, EventArgs e) { RefreshArrayInfo(); })));
            lblArrayInfo = CardLabel("", 960);
            f5.Controls.Add(Row(22, lblArrayInfo));
            c5.Body.Controls.Add(f5);
            c5.Location = new Point(20, 780);
            flow.Controls.Add(c5);

            Card c6 = new Card("只读：结局相关状态（不提供修改）", 1024, 78);
            lblEnding = CardLabel("说明：待选结局 / 结局位属于存档一致性配对字段，本工具不提供修改，避免存档损坏。", 960);
            lblEnding.Location = new Point(12, 8);
            c6.Body.Controls.Add(lblEnding);
            c6.Location = new Point(20, 962);
            flow.Controls.Add(c6);

            page.Controls.Add(flow);
            return page;
        }

        private void BuildLocks()
        {
            LockSpec sperm = new LockSpec();
            sperm.Name = "资源锁";
            sperm.Apply = delegate(IMemory m, Chain c)
            {
                double want;
                if (!double.TryParse(txtSpermLock.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out want))
                    return "资源锁数值无效";
                double cur;
                if (!M.ReadF64(m, c.Save + (ulong)Off.CurrentSperm, out cur)) return "资源锁读取失败";
                if (Math.Abs(cur - want) < 0.001) return null;
                if (!Limits.F64(want, 0d, 1e12)) return "资源锁数值超出范围";
                if (!WriteF64Safe(m, c.Save + (ulong)Off.CurrentSperm, want)) return "资源锁写入失败";
                return null;
            };
            locks["sperm"] = sperm;

            LockSpec stam = new LockSpec();
            stam.Name = "全耐力锁";
            stam.Apply = delegate(IMemory m, Chain c)
            {
                ulong mc = M.PtrOr0(m, c.Holder + (ulong)Off.HolderMainChar);
                if (!Addr.Valid(mc)) return "角色尚未进入地图，全耐力锁暂不可用";
                string cn = M.ClassName(m, mc);
                if (cn == null || cn.IndexOf("MainCharStateControl", StringComparison.Ordinal) < 0)
                    return "角色对象校验失败，全耐力锁暂不可用";
                float max;
                if (!M.ReadF32(m, mc + (ulong)Off.MainCharMaxStamina, out max)) return "无法读取角色最大耐力";
                if (!Limits.F32(max, 1f, 100000f)) return "角色最大耐力异常，已跳过";
                float cur;
                if (!M.ReadF32(m, mc + (ulong)Off.MainCharCurrentStamina, out cur)) return "无法读取角色当前耐力";
                if (Math.Abs(cur - max) < 0.01f) return null;
                if (!WriteF32Safe(m, mc + (ulong)Off.MainCharCurrentStamina, max)) return "耐力写入失败";
                return null;
            };
            locks["stamina"] = stam;

            LockSpec date = new LockSpec();
            date.Name = "无限天数";
            date.Apply = delegate(IMemory m, Chain c)
            {
                int want;
                if (!int.TryParse(txtDateLock.Text.Trim(), out want)) return "日期锁数值无效";
                if (!Limits.I32(want, 1, 1000000)) return "日期锁需在 1..1000000";
                int cur;
                if (!M.ReadI32(m, c.Save + (ulong)Off.CurrentDate, out cur)) return "日期锁读取失败";
                if (cur == want) return null;
                if (!WriteI32Safe(m, c.Save + (ulong)Off.CurrentDate, want)) return "日期锁写入失败";
                return null;
            };
            locks["date"] = date;
        }

        // ------------------------------------------------------------ 生命周期
        public void StartMonitoring()
        {
            timer = new System.Windows.Forms.Timer();
            timer.Interval = Cfg.PollMs;
            timer.Tick += Tick;
            timer.Start();
            Tick(null, null);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            closing = true;
            if (timer != null) { timer.Stop(); timer.Dispose(); timer = null; }
            activeLocks.Clear();
            if (mem != null) { mem.Dispose(); mem = null; }
            base.OnFormClosing(e);
        }

        // ------------------------------------------------------------ 连接
        private bool Attach(out string err)
        {
            err = null;
            Process[] ps;
            try { ps = Process.GetProcessesByName(Cfg.ProcessName); }
            catch (Exception ex) { err = "枚举进程失败：" + ex.Message; return false; }

            Process target = null;
            foreach (Process p in ps)
            {
                try
                {
                    string f = p.MainModule.FileName;
                    string d = Path.GetDirectoryName(f).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    if (string.Equals(d, gameDir, StringComparison.OrdinalIgnoreCase)) { target = p; break; }
                }
                catch { }
            }
            if (target == null) { err = "未找到游戏进程（需要在游戏目录中运行本修改器）"; Detach(); return false; }

            if (pid != target.Id || mem == null)
            {
                Detach();
                pid = target.Id;
                mem = new ProcessMemory(pid);
                hashOk = false;
                hashPid = 0;
                gen = null;
                undo.Clear();
                undoBytes = 0;
                activeLocks.Clear();
            }

            ulong ga;
            if (!FindModuleBase(target, out ga)) { err = "未找到 GameAssembly.dll 模块"; Detach(); return false; }
            gaBase = ga;

            if (!hashOk || hashPid != pid)
            {
                if (!VerifyHash(out err)) return false;
                hashOk = true;
                hashPid = pid;
            }
            return true;
        }

        private static bool FindModuleBase(Process p, out ulong ga)
        {
            ga = 0;
            try
            {
                foreach (ProcessModule mod in p.Modules)
                {
                    if (string.Equals(mod.ModuleName, Cfg.AssemblyName, StringComparison.OrdinalIgnoreCase))
                    {
                        ga = unchecked((ulong)mod.BaseAddress.ToInt64());
                        return ga != 0;
                    }
                }
            }
            catch { }
            return false;
        }

        private bool VerifyHash(out string err)
        {
            err = null;
            string path = Path.Combine(gameDir, Cfg.AssemblyName);
            try
            {
                FileInfo fi = new FileInfo(path);
                if (!fi.Exists) { err = "找不到 GameAssembly.dll"; return false; }
                if (fi.Length != Cfg.AssemblySize)
                {
                    err = "GameAssembly.dll 大小不符（期望 " + Cfg.AssemblySize + "，实际 " + fi.Length + "），已安全停止";
                    return false;
                }
                string hash;
                using (SHA256 sha = SHA256.Create())
                using (FileStream fs = File.OpenRead(path))
                {
                    byte[] h = sha.ComputeHash(fs);
                    StringBuilder sb = new StringBuilder(h.Length * 2);
                    for (int i = 0; i < h.Length; i++) sb.Append(h[i].ToString("X2", CultureInfo.InvariantCulture));
                    hash = sb.ToString();
                }
                if (!string.Equals(hash, Cfg.AssemblySha256, StringComparison.OrdinalIgnoreCase))
                {
                    err = "GameAssembly.dll 校验失败，版本不匹配，已安全停止";
                    return false;
                }
                return true;
            }
            catch (Exception ex) { err = "校验失败：" + ex.Message; return false; }
        }

        private void Detach()
        {
            activeLocks.Clear();
            undo.Clear();
            undoBytes = 0;
            gen = null;
            hashOk = false;
            hashPid = 0;
            gaBase = 0;
            pid = 0;
            if (mem != null) { mem.Dispose(); mem = null; }
        }

        private bool FreshChain(out Chain c, out string err)
        {
            c = null;
            err = null;
            string aerr;
            if (!Attach(out aerr)) { err = aerr; return false; }
            return Resolver.Resolve(mem, gaBase, out c, out err);
        }

        // 进程启动时间作为身份的一部分：即使 PID 被复用也能识别出这是新进程
        private long ProcStart()
        {
            if (procStartPid == pid && pid != 0) return procStartTicks;
            long t = 0;
            try
            {
                using (System.Diagnostics.Process p = System.Diagnostics.Process.GetProcessById(pid))
                    t = p.StartTime.Ticks;
            }
            catch { t = 0; }
            procStartPid = pid;
            procStartTicks = t;
            return t;
        }

        private bool BuildSnapshot(out Snapshot s, out string err)
        {
            s = null;
            err = null;
            Chain c;
            if (!FreshChain(out c, out err)) return false;
            Snapshot ns = new Snapshot();
            ns.Connected = true;
            ns.Pid = pid;
            ns.HashOk = hashOk;
            ns.GameAssembly = gaBase;
            ns.Holder = c.Holder;
            ns.HolderClass = c.HolderClass;
            ns.Save = c.Save;
            ns.SaveClass = c.SaveClass;
            ns.SaveLoaded = c.Save != 0;
            if (c.Save != 0) Data.ReadBasics(mem, c, ns);
            s = ns;
            return true;
        }

        // ------------------------------------------------------------ 轮询
        private void Tick(object sender, EventArgs e)
        {
            if (busy || closing) return;
            busy = true;
            try
            {
                Snapshot ns;
                string err;
                if (!BuildSnapshot(out ns, out err))
                {
                    SetStatus("未连接：" + err, false);
                    activeLocks.Clear();
                    undo.Clear();
                    undoBytes = 0;
                    gen = null;
                    SetWritesEnabled(false);
                    snap = new Snapshot();
                    ClearDynamic();
                    return;
                }

                long start = ProcStart();
                bool genChanged = gen == null || !gen.Same(ns.Pid, start, ns.Holder, ns.Save);
                if (genChanged)
                {
                    // 存档/进程身份变化：停止锁定，清空撤销，绝不写入旧指针
                    activeLocks.Clear();
                    undo.Clear();
                    undoBytes = 0;
                    gen = new Gen(ns.Pid, start, ns.Holder, ns.Save);
                    chkSpermLock.Checked = false;
                    chkStamLock.Checked = false;
                    chkDateLock.Checked = false;
                }
                snap = ns;
                SetWritesEnabled(ns.SaveLoaded);

                string lockMsg = null;
                if (ns.SaveLoaded && activeLocks.Count > 0)
                {
                    Chain c;
                    string cerr;
                    if (FreshChain(out c, out cerr) && c.Save != 0) lockMsg = ApplyLocks(c, false);
                }

                if (!ns.SaveLoaded)
                {
                    SetStatus("已连接 PID " + ns.Pid + " · 等待载入存档", true);
                }
                else
                {
                    int[] owned = DressCatalog.OwnedPieces(ns.Items);
                    int ownN = DressCatalog.OwnedSeriesCount(owned);
                    int trackN = DressCatalog.TrackableSeriesCount();
                    SetStatus("已连接 · 存档已载入 · 服装 " + ownN + "/" + trackN, true);
                }

                UpdateDynamic(ns);
                if (lockMsg != null) SetStatus("锁定提示：" + lockMsg, false);
            }
            finally { busy = false; }
        }

        private string ApplyLocks(Chain c, bool verbose)
        {
            string last = null;
            foreach (string key in new List<string>(activeLocks))
            {
                LockSpec ls;
                if (!locks.TryGetValue(key, out ls)) continue;
                string r = ls.Apply(mem, c);
                if (r != null) last = ls.Name + "：" + r;
            }
            return last;
        }

        private void ToggleLock(string key, bool on)
        {
            if (!on) { activeLocks.Remove(key); SetStatus("已关闭锁定", true); return; }
            string err;
            Chain c;
            if (!FreshChain(out c, out err) || c.Save == 0)
            {
                SetStatus("无法启用锁定：" + (err == null ? "存档未载入" : err), false);
                SetLockChecked(key, false);
                return;
            }
            if (!mem.EnsureWrite())
            {
                SetStatus("无法获取写入权限，锁定未启用", false);
                SetLockChecked(key, false);
                return;
            }
            activeLocks.Add(key);
            string r = locks[key].Apply(mem, c);
            if (r == null) SetStatus("已启用 " + locks[key].Name, true);
            else
            {
                SetStatus(locks[key].Name + "：" + r, false);
                if (key == "stamina") { activeLocks.Remove(key); SetLockChecked(key, false); }
            }
        }

        private void SetLockChecked(string key, bool v)
        {
            if (key == "sperm") chkSpermLock.Checked = v;
            if (key == "stamina") chkStamLock.Checked = v;
            if (key == "date") chkDateLock.Checked = v;
        }

        private void SetStatus(string text, bool ok)
        {
            if (closing) return;
            int cut = text.IndexOf('\n');
            statusPill.Set(cut > 0 ? text.Substring(0, cut) : text, ok ? Theme.Ok : Theme.Bad, ok ? Theme.OkBack : Theme.BadBack);
        }

        private void SetWritesEnabled(bool on)
        {
            // on=false：未连接或未载入存档。禁用所有可写控件；刷新与本地备份保持可用。
            bool stam = on && snap != null && snap.MainCharPresent;
            foreach (Control c in writeControls)
            {
                if (c == chkStamLock) { c.Enabled = stam; continue; }
                c.Enabled = on;
            }
            if (chkStamLock != null) chkStamLock.Enabled = stam;
            btnRefresh.Enabled = true;
            if (btnBackup != null) btnBackup.Enabled = true;
            if (btnUndo != null) btnUndo.Enabled = true;
        }

        // ------------------------------------------------------------ 写入路径
        private delegate string MutateFn(IMemory m, Chain c);

        private bool Mutate(string label, MutateFn fn)
        {
            Chain c;
            string err;
            if (!FreshChain(out c, out err)) { SetStatus("修改失败：" + err, false); return false; }
            if (c.Save == 0) { SetStatus("尚未载入存档，无法修改", false); return false; }
            if (mem == null || !mem.EnsureWrite())
            {
                SetStatus("无法获取写入权限（请以相同权限运行，必要时用管理员启动）", false);
                return false;
            }
            long start = ProcStart();
            if (gen != null && !gen.Same(pid, start, c.Holder, c.Save))
            {
                // 界面显示的快照已过期：拒绝本次写入，避免落到刚切换的存档上
                activeLocks.Clear();
                undo.Clear();
                undoBytes = 0;
                gen = new Gen(pid, start, c.Holder, c.Save);
                chkSpermLock.Checked = false;
                chkStamLock.Checked = false;
                chkDateLock.Checked = false;
                SetStatus("存档/进程已切换，已清除锁定与撤销；请先刷新确认后再修改", false);
                Tick(null, null);
                return false;
            }
            if (gen == null) gen = new Gen(pid, start, c.Holder, c.Save);
            string r = fn(mem, c);
            if (r != null) { SetStatus("修改失败：" + r, false); return false; }
            SetStatus("已应用：" + label, true);
            Tick(null, null);
            return true;
        }

        // SaveData 内已知的基元字段（偏移, 字节数）。撤销只覆盖这些字段；
        // 物品/任务/角色/数组等对象不进入撤销缓冲。
        private static readonly int[] UndoOffsets = new int[]
        {
            0x38, 0x3C, 0x44, 0x48, 0x50, 0x58, 0x5C, 0x60, 0xE8, 0xF8, 0xFC,
            0x100, 0x104, 0x118, 0x11C, 0x120, 0x124, 0x128, 0x138, 0x148, 0x14C,
            0x154, 0x158, 0x15C, 0x160, 0x164, 0x168, 0x16C, 0x16D
        };
        private static readonly int[] UndoSizes = new int[]
        {
            4, 4, 4, 4, 8, 4, 4, 4, 4, 4, 4,
            4, 4, 4, 4, 4, 4, 4, 4, 4, 4,
            4, 4, 4, 4, 4, 4, 1, 1
        };

        private bool RecordUndo(IMemory m, ulong addr, int len)
        {
            if (gen == null || len <= 0) return false;
            if (addr < gen.Save) return false;
            ulong rel = addr - gen.Save;
            if (rel > 0xFFFFUL) return false;
            int off = (int)rel;
            bool known = false;
            for (int i = 0; i < UndoOffsets.Length; i++)
            {
                if (off >= UndoOffsets[i] && off + len <= UndoOffsets[i] + UndoSizes[i]) { known = true; break; }
            }
            if (!known) return false;
            // 同一地址只保留第一次（最早）的原始字节，反复锁定不会覆盖历史
            ulong aEnd = addr + (ulong)len;
            for (int i = 0; i < undo.Count; i++)
            {
                UndoEntry e = undo[i];
                if (e.Addr == addr) return e.Bytes.Length == len;
                ulong eEnd = e.Addr + (ulong)e.Bytes.Length;
                if (addr < eEnd && e.Addr < aEnd) return false;
            }
            if (!Addr.Range(addr, len)) return false;
            if (undo.Count >= 512 || undoBytes + len > (1 << 20)) return false;
            byte[] b = new byte[len];
            if (!m.ReadBytes(addr, b, 0, len)) return false;
            UndoEntry u = new UndoEntry();
            u.Addr = addr;
            u.Bytes = b;
            undo.Add(u);
            undoBytes += len;
            return true;
        }

        private bool WriteI32Safe(IMemory m, ulong addr, int v)
        {
            RecordUndo(m, addr, 4);
            if (!M.WriteI32(m, addr, v)) return false;
            int back;
            return M.ReadI32(m, addr, out back) && back == v;
        }
        private bool WriteF32Safe(IMemory m, ulong addr, float v)
        {
            RecordUndo(m, addr, 4);
            if (!M.WriteF32(m, addr, v)) return false;
            float back;
            return M.ReadF32(m, addr, out back) && back == v;
        }
        private bool WriteF64Safe(IMemory m, ulong addr, double v)
        {
            RecordUndo(m, addr, 8);
            if (!M.WriteF64(m, addr, v)) return false;
            double back;
            return M.ReadF64(m, addr, out back) && back == v;
        }
        private bool WriteBoolSafe(IMemory m, ulong addr, bool v)
        {
            RecordUndo(m, addr, 1);
            if (!M.WriteBool(m, addr, v)) return false;
            bool back;
            return M.ReadBool(m, addr, out back) && back == v;
        }

        private void DoUndo()
        {
            if (undo.Count == 0) { SetStatus("没有可撤销的本次会话修改", false); return; }
            Chain c;
            string err;
            if (!FreshChain(out c, out err)) { SetStatus("撤销失败：" + err, false); return; }
            if (gen == null || !gen.Same(pid, ProcStart(), c.Holder, c.Save))
            {
                undo.Clear();
                undoBytes = 0;
                SetStatus("存档身份已变化，撤销数据已清空", false);
                return;
            }
            // 先停止所有锁定并取消勾选，避免轮询在恢复过程中把锁定值重新写回
            activeLocks.Clear();
            chkSpermLock.Checked = false;
            chkStamLock.Checked = false;
            chkDateLock.Checked = false;
            int n = 0;
            for (int i = undo.Count - 1; i >= 0; i--)
            {
                UndoEntry u = undo[i];
                if (mem.WriteBytes(u.Addr, u.Bytes, 0, u.Bytes.Length)) n++;
            }
            undo.Clear();
            undoBytes = 0;
            SetStatus("已撤销 " + n + " 处（范围：仅存档基元字段）", true);
            Tick(null, null);
        }

        // ------------------------------------------------------------ 应用动作
        private void ApplyGold()
        {
            Mutate("金币", delegate(IMemory m, Chain c)
            {
                int v;
                if (!int.TryParse(txtGold.Text.Trim(), out v)) return "金币必须是整数";
                if (!Limits.I32(v, 0, 2000000000)) return "金币需在 0..2000000000";
                if (!WriteI32Safe(m, c.Save + (ulong)Off.Gold, v)) return "金币写入校验失败";
                return null;
            });
        }

        private void ApplySperm()
        {
            Mutate("精液储量 / 收集资源", delegate(IMemory m, Chain c)
            {
                double v;
                if (!double.TryParse(txtSperm.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return "资源必须是数字";
                if (!Limits.F64(v, 0d, 1e12)) return "资源需为 0..1e12 的有限数";
                if (!WriteF64Safe(m, c.Save + (ulong)Off.CurrentSperm, v)) return "资源写入校验失败";
                return null;
            });
        }

        private void ApplyDate()
        {
            Mutate("当前日期", delegate(IMemory m, Chain c)
            {
                int v;
                if (!int.TryParse(txtDate.Text.Trim(), out v)) return "日期必须是整数";
                if (!Limits.I32(v, 1, 1000000)) return "日期需在 1..1000000";
                if (!WriteI32Safe(m, c.Save + (ulong)Off.CurrentDate, v)) return "日期写入校验失败";
                return null;
            });
        }

        private void ApplyDifficulty()
        {
            Mutate("难度", delegate(IMemory m, Chain c)
            {
                int v = cboDiff.SelectedIndex;
                if (!Limits.I32(v, 0, 2)) return "难度取值非法";
                if (!WriteI32Safe(m, c.Save + (ulong)Off.Difficulty, v)) return "难度写入校验失败";
                return null;
            });
        }

        private void ApplyBodySize()
        {
            Mutate("体型", delegate(IMemory m, Chain c)
            {
                int v = cboBody.SelectedIndex;
                if (!Limits.I32(v, 0, 4)) return "体型取值非法";
                if (!WriteI32Safe(m, c.Save + (ulong)Off.CurrentBodySize, v)) return "体型写入校验失败";
                return null;
            });
        }

        private void ApplyMaxStamina()
        {
            Mutate("最大耐力", delegate(IMemory m, Chain c)
            {
                float v;
                if (!float.TryParse(txtMaxStam.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return "最大耐力必须是数字";
                if (!Limits.F32(v, 1f, 100000f)) return "最大耐力需在 1..100000";
                if (!WriteF32Safe(m, c.Save + (ulong)Off.MaxStamina, v)) return "最大耐力写入校验失败";
                return null;
            });
        }

        private void ApplyAroused()
        {
            Mutate("兴奋度等级", delegate(IMemory m, Chain c)
            {
                int v;
                if (!int.TryParse(txtAroused.Text.Trim(), out v)) return "兴奋度必须是整数";
                if (!Limits.I32(v, 0, 100000)) return "兴奋度需在 0..100000";
                if (!WriteI32Safe(m, c.Holder + (ulong)Off.HolderArousedLevel, v)) return "兴奋度写入校验失败";
                return null;
            });
        }

        private void ApplyI32(string label, int offset, TextBox box, int lo, int hi)
        {
            Mutate(label, delegate(IMemory m, Chain c)
            {
                int v;
                if (!int.TryParse(box.Text.Trim(), out v)) return label + " 必须是整数";
                if (!Limits.I32(v, lo, hi)) return label + " 需在 " + lo + ".." + hi;
                if (!WriteI32Safe(m, c.Save + (ulong)offset, v)) return label + " 写入校验失败";
                return null;
            });
        }

        private void ApplyBoolToggle(string label, int offset, bool value)
        {
            if (suppressCheck) return;
            Mutate(label, delegate(IMemory m, Chain c)
            {
                if (!WriteBoolSafe(m, c.Save + (ulong)offset, value)) return label + " 写入校验失败";
                return null;
            });
        }

        private static bool TryParseMask(string text, out int v)
        {
            v = 0;
            string t = text == null ? "" : text.Trim();
            if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) t = t.Substring(2);
            if (t.Length == 0) return false;
            return int.TryParse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v);
        }

        private void ApplyMask(string label, int offset, TextBox box)
        {
            Mutate(label, delegate(IMemory m, Chain c)
            {
                int v;
                if (!TryParseMask(box.Text, out v)) return label + " 需要十六进制数值（如 0x1F）";
                if (!WriteI32Safe(m, c.Save + (ulong)offset, v)) return label + " 写入校验失败";
                return null;
            });
        }

        // ---- 背包 ----
        private void RefreshInventory(bool show)
        {
            if (lvItems == null) return;
            lvItems.BeginUpdate();
            lvItems.Items.Clear();
            List<ItemView> items;
            string err;
            Chain c;
            string cerr;
            if (!FreshChain(out c, out cerr) || c.Save == 0)
            {
                lvItems.EndUpdate();
                if (show) SetStatus("背包读取失败：" + (cerr == null ? "存档未载入" : cerr), false);
                return;
            }
            if (!Data.ReadInventory(mem, c, out items, out err))
            {
                lvItems.EndUpdate();
                if (show) SetStatus("背包读取失败：" + err, false);
                return;
            }
            foreach (ItemView it in items)
            {
                ListViewItem li = new ListViewItem(new string[] {
                    it.Name.Length == 0 ? "(无名称)" : it.Name,
                    it.DbId.ToString(CultureInfo.InvariantCulture),
                    it.ItemType.ToString(CultureInfo.InvariantCulture),
                    Labels.InvTypeName(it.InvType),
                    it.Count.ToString(CultureInfo.InvariantCulture),
                    it.MaxCount.ToString(CultureInfo.InvariantCulture),
                    Labels.StateName(it.State) + (it.Purchased ? " / 已购入" : ""),
                    it.X + "," + it.Y
                });
                li.Tag = it;
                li.ForeColor = it.State == 2 ? Theme.Accent : Theme.Text;
                lvItems.Items.Add(li);
            }
            lvItems.EndUpdate();
            lblInvInfo.Text = "共 " + items.Count + " 件物品（只列出既有物品，不新增/删除/移动网格/装备）。粉色行 = 当前已装备。";
        }

        private void ApplyItemCount()
        {
            if (lvItems == null || lvItems.SelectedItems.Count == 0) { SetStatus("请先在列表中选择一件物品", false); return; }
            ItemView sel = lvItems.SelectedItems[0].Tag as ItemView;
            if (sel == null) { SetStatus("选中行已失效，请刷新列表", false); return; }
            int want;
            if (!int.TryParse(txtItemCount.Text.Trim(), out want)) { SetStatus("数量必须是整数", false); return; }
            Mutate("物品数量", delegate(IMemory m, Chain c)
            {
                List<ItemView> live;
                string err;
                if (!Data.ReadInventory(m, c, out live, out err)) return "背包读取失败：" + err;
                ItemView cur = FindItem(live, sel);
                if (cur == null) return "对象身份已变化（存档或列表已刷新），请重新刷新列表后重试";
                if (cur.MaxCount > 0)
                {
                    if (!Limits.I32(want, 0, cur.MaxCount)) return "数量需在 0.." + cur.MaxCount + "（该物品上限）";
                }
                else
                {
                    if (!Limits.I32(want, 0, 1000000)) return "数量需在 0..1000000";
                }
                if (!WriteI32Safe(m, cur.Ptr + (ulong)Off.ItemCount, want)) return "数量写入校验失败";
                return null;
            });
            RefreshInventory(false);
        }

        private static ItemView FindItem(List<ItemView> live, ItemView sel)
        {
            for (int i = 0; i < live.Count; i++)
            {
                if (live[i].Ptr == sel.Ptr && live[i].Index == sel.Index) return live[i];
            }
            return null;
        }

        private void RefillSelected()
        {
            if (lvItems == null || lvItems.SelectedItems.Count == 0) { SetStatus("请先在列表中选择一件物品", false); return; }
            ItemView sel = lvItems.SelectedItems[0].Tag as ItemView;
            if (sel == null) { SetStatus("选中行已失效，请刷新列表", false); return; }
            bool ok = Mutate("补满物品", delegate(IMemory m, Chain c)
            {
                List<ItemView> live;
                string err;
                if (!Data.ReadInventory(m, c, out live, out err)) return "背包读取失败：" + err;
                ItemView cur = FindItem(live, sel);
                if (cur == null) return "对象身份已变化，请刷新列表";
                if (cur.MaxCount <= 0) return "该物品没有可用上限";
                if (!Limits.I32(cur.MaxCount, 1, 1000000)) return "上限异常：" + cur.MaxCount;
                if (!WriteI32Safe(m, cur.Ptr + (ulong)Off.ItemCount, cur.MaxCount)) return "写入校验失败";
                return null;
            });
            SetStatus(ok ? "已补满选中物品" : "无法补满：对象身份变化或该物品没有上限", ok);
            RefreshInventory(false);
        }

        private void RefillConsumables()
        {
            Mutate("补满消耗品堆", delegate(IMemory m, Chain c)
            {
                List<ItemView> live;
                string err;
                if (!Data.ReadInventory(m, c, out live, out err)) return "背包读取失败：" + err;
                int n = 0;
                foreach (ItemView it in live)
                {
                    if (it.InvType != 1 && it.InvType != 10 && it.InvType != 11) continue;
                    if (it.MaxCount <= 0) continue;
                    if (it.Count >= it.MaxCount) continue;
                    if (!WriteI32Safe(m, it.Ptr + (ulong)Off.ItemCount, it.MaxCount)) continue;
                    n++;
                }
                if (n == 0) return "没有需要补满的既有堆叠";
                return null;
            });
            RefreshInventory(false);
        }

        /// <summary>把选中物品移动到指定容器（1=背包 3=仓库）。只写既有 InvType 字段。</summary>
        private void MoveSelectedContainer(int targetInvType)
        {
            if (lvItems == null || lvItems.SelectedItems.Count == 0) { SetStatus("请先在列表中选择一件物品", false); return; }
            ItemView sel = lvItems.SelectedItems[0].Tag as ItemView;
            if (sel == null) { SetStatus("选中行已失效，请刷新列表", false); return; }
            string label = targetInvType == 1 ? "移到背包" : "移到仓库";
            Mutate(label, delegate(IMemory m, Chain c)
            {
                List<ItemView> live;
                string err;
                if (!Data.ReadInventory(m, c, out live, out err)) return "背包读取失败：" + err;
                ItemView cur = FindItem(live, sel);
                if (cur == null) return "对象身份已变化，请刷新列表";
                if (cur.State == 2) return "该物品处于已装备状态，请先在游戏内脱下再移动";
                if (cur.InvType == targetInvType) return "该物品已在该容器中";
                if (!Limits.I32(targetInvType, 1, 3)) return "目标容器非法";
                if (!WriteI32Safe(m, cur.Ptr + (ulong)Off.ItemInvType, targetInvType)) return "容器字段写入校验失败";
                return null;
            });
            RefreshInventory(false);
        }

        // ---- 任务 ----
        private void RefreshQuests(bool show)
        {
            if (lvQuests == null) return;
            lvQuests.BeginUpdate();
            lvQuests.Items.Clear();
            Chain c;
            string cerr;
            if (!FreshChain(out c, out cerr) || c.Save == 0)
            {
                lvQuests.EndUpdate();
                if (show) SetStatus("任务读取失败：" + (cerr == null ? "存档未载入" : cerr), false);
                return;
            }
            List<QuestView> qs;
            string err;
            if (!Data.ReadQuests(mem, c, out qs, out err))
            {
                lvQuests.EndUpdate();
                if (show) SetStatus("任务读取失败：" + err, false);
                return;
            }
            foreach (QuestView q in qs)
            {
                ListViewItem li = new ListViewItem(new string[] {
                    q.Name.Length == 0 ? "(无名称)" : q.Name,
                    q.Id.ToString(CultureInfo.InvariantCulture),
                    q.Level.ToString(CultureInfo.InvariantCulture),
                    q.Current.ToString(CultureInfo.InvariantCulture),
                    q.MaxCount.ToString(CultureInfo.InvariantCulture)
                });
                li.Tag = q;
                lvQuests.Items.Add(li);
            }
            lvQuests.EndUpdate();
            lblQuestInfo.Text = "共 " + qs.Count + " 条任务记录；写满进度后仍需回游戏正常提交才能领取奖励。";
        }

        private void ApplyQuestProgress()
        {
            if (lvQuests == null || lvQuests.SelectedItems.Count == 0) { SetStatus("请先选择一条任务", false); return; }
            QuestView sel = lvQuests.SelectedItems[0].Tag as QuestView;
            if (sel == null) { SetStatus("选中行已失效，请刷新列表", false); return; }
            int want;
            if (!int.TryParse(txtQuestProgress.Text.Trim(), out want)) { SetStatus("进度必须是整数", false); return; }
            Mutate("任务进度", delegate(IMemory m, Chain c)
            {
                return WriteQuest(m, c, sel, want);
            });
            RefreshQuests(false);
        }

        private void CompleteQuestSelected()
        {
            if (lvQuests == null || lvQuests.SelectedItems.Count == 0) { SetStatus("请先选择一条任务", false); return; }
            QuestView sel = lvQuests.SelectedItems[0].Tag as QuestView;
            if (sel == null) { SetStatus("选中行已失效，请刷新列表", false); return; }
            Mutate("完成任务计数", delegate(IMemory m, Chain c)
            {
                List<QuestView> live;
                string err;
                if (!Data.ReadQuests(m, c, out live, out err)) return "任务读取失败：" + err;
                QuestView cur = FindQuest(live, sel);
                if (cur == null) return "对象身份已变化，请刷新列表";
                if (cur.MaxCount <= 0) return "该任务没有可用需求数量";
                return WriteQuest(m, c, cur, cur.MaxCount);
            });
            RefreshQuests(false);
        }

        private string WriteQuest(IMemory m, Chain c, QuestView sel, int want)
        {
            List<QuestView> live;
            string err;
            if (!Data.ReadQuests(m, c, out live, out err)) return "任务读取失败：" + err;
            QuestView cur = FindQuest(live, sel);
            if (cur == null) return "对象身份已变化，请刷新列表";
            if (!Limits.I32(want, 0, 1000000)) return "进度需在 0..1000000";
            if (cur.MaxCount > 0 && want > cur.MaxCount) return "进度不能超过需求数量 " + cur.MaxCount;
            if (!WriteI32Safe(m, cur.Ptr + (ulong)Off.QuestCurrent, want)) return "进度写入校验失败";
            return null;
        }

        private static QuestView FindQuest(List<QuestView> live, QuestView sel)
        {
            for (int i = 0; i < live.Count; i++)
            {
                if (live[i].Ptr == sel.Ptr && live[i].Index == sel.Index) return live[i];
            }
            return null;
        }

        // ---- 服装 ----
        private int lastDressGen = -1;

        private void RefreshWardrobe(List<ItemView> items)
        {
            if (lvDress == null) return;
            int[] owned = DressCatalog.OwnedPieces(items);
            lvDress.BeginUpdate();
            lvDress.Items.Clear();
            List<string> missing = new List<string>();
            for (int i = 0; i < DressCatalog.All.Length; i++)
            {
                DressCatalog.Series s = DressCatalog.All[i];
                StringBuilder parts = new StringBuilder();
                for (int k = 0; k < s.Parts.Length; k++)
                {
                    if (k > 0) parts.Append(" / ");
                    parts.Append(Labels.PartName(s.Parts[k]));
                }
                bool trackable = !string.IsNullOrEmpty(s.Match);
                bool own = trackable && owned[i] > 0;
                string state;
                if (!trackable) state = "待确认";
                else state = own ? "已拥有" : "未解锁";
                ListViewItem li = new ListViewItem(new string[] {
                    s.Key, s.Zh,
                    s.Colors.Length + " 色",
                    parts.ToString(),
                    state,
                    trackable ? owned[i].ToString(CultureInfo.InvariantCulture) : "-",
                    s.Key == "Bunny_Police" ? "解锁标志位可在「进阶数据」页勾选" : (own ? "" : "商店 / 地图掉落 / 事件")
                });
                li.ForeColor = own ? Theme.Ok : (!trackable ? Theme.Sub : Theme.Bad);
                lvDress.Items.Add(li);
                if (trackable && !own) missing.Add(s.Zh);
            }
            lvDress.EndUpdate();

            int ownN = 0, trackN = 0;
            for (int i = 0; i < DressCatalog.All.Length; i++)
            {
                if (string.IsNullOrEmpty(DressCatalog.All[i].Match)) continue;
                trackN++;
                if (owned[i] > 0) ownN++;
            }

            // 发型
            int hairN = 0;
            StringBuilder hairs = new StringBuilder();
            if (items != null)
            {
                HashSet<int> hairIds = new HashSet<int>();
                foreach (ItemView it in items)
                {
                    if (it.InvType != 5) continue;
                    if (hairIds.Add(it.DbId))
                    {
                        if (hairs.Length > 0) hairs.Append("、");
                        hairs.Append(it.Name.Length == 0 ? ("#" + it.DbId) : it.Name);
                        hairN++;
                    }
                }
            }

            StringBuilder miss = new StringBuilder();
            for (int i = 0; i < missing.Count; i++)
            {
                if (i > 0 && i % 8 == 0) miss.Append("\n");
                else if (i > 0) miss.Append("、");
                miss.Append(missing[i]);
            }
            lblDressInfo.Text =
                "已拥有 " + ownN + " / " + trackN + " 个系列；未解锁 " + missing.Count + " 系列：\n" +
                (missing.Count > 0 ? miss.ToString() : "（全部系列均已拥有）") +
                (hairN > 0 ? "\n发型收藏（" + hairN + "）：" + hairs : "");
        }

        private static string JoinStr(List<string> list, string sep)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < list.Count; i++)
            {
                if (i > 0) sb.Append(sep);
                sb.Append(list[i]);
            }
            return sb.ToString();
        }

        // ---- 数组 ----
        private static int ArrayOffset(int idx)
        {
            switch (idx)
            {
                case 0: return Off.D0;
                case 1: return Off.A0;
                case 2: return Off.A8;
                case 3: return Off.B0;
                case 4: return Off.B8;
                case 5: return Off.C0;
                default: return Off.C8;
            }
        }

        private void AutoKind()
        {
            if (cboArrayField == null || cboArrayKind == null) return;
            int i = cboArrayField.SelectedIndex;
            cboArrayKind.SelectedIndex = (i == 5 || i == 6) ? 1 : 0;
            RefreshArrayInfo();
        }

        private void RefreshArrayInfo()
        {
            if (lblArrayInfo == null) return;
            Chain c;
            string cerr;
            if (!FreshChain(out c, out cerr) || c.Save == 0)
            {
                lblArrayInfo.Text = "数组信息不可用：" + (cerr == null ? "存档未载入" : cerr);
                return;
            }
            ulong arr;
            int len;
            string err;
            ArrayKind kind = cboArrayKind.SelectedIndex == 1 ? ArrayKind.F64 : ArrayKind.I32;
            if (!Data.ReadArray(mem, c, ArrayOffset(cboArrayField.SelectedIndex), kind, out arr, out len, out err))
            {
                lblArrayInfo.Text = "数组信息不可用：" + err;
                return;
            }
            lblArrayInfo.Text = "数组长度 " + len + "，上限 " + Cfg.MaxArrayElems + "，元素类型 " + (kind == ArrayKind.F64 ? "double" : "int32") + "。";
        }

        private void ReadArrayValue()
        {
            Chain c;
            string cerr;
            if (!FreshChain(out c, out cerr) || c.Save == 0) { SetStatus("读取失败：" + (cerr == null ? "存档未载入" : cerr), false); return; }
            int idx;
            if (!int.TryParse(txtArrayIndex.Text.Trim(), out idx)) { SetStatus("下标必须是整数", false); return; }
            ulong arr;
            int len;
            string err;
            ArrayKind kind = cboArrayKind.SelectedIndex == 1 ? ArrayKind.F64 : ArrayKind.I32;
            if (!Data.ReadArray(mem, c, ArrayOffset(cboArrayField.SelectedIndex), kind, out arr, out len, out err))
            {
                SetStatus("读取失败：" + err, false);
                return;
            }
            if (idx < 0 || idx >= len) { SetStatus("下标越界（数组长度 " + len + "）", false); return; }
            ulong ea = arr + (ulong)Off.ArrayData + (ulong)(idx * (int)kind);
            if (kind == ArrayKind.F64)
            {
                double d;
                if (!M.ReadF64(mem, ea, out d)) { SetStatus("读取失败", false); return; }
                txtArrayValue.Text = d.ToString("R", CultureInfo.InvariantCulture);
            }
            else
            {
                int v;
                if (!M.ReadI32(mem, ea, out v)) { SetStatus("读取失败", false); return; }
                txtArrayValue.Text = v.ToString(CultureInfo.InvariantCulture);
            }
            SetStatus("已读取数组元素", true);
        }

        private void ApplyArrayValue()
        {
            int field = cboArrayField.SelectedIndex;
            ArrayKind kind = cboArrayKind.SelectedIndex == 1 ? ArrayKind.F64 : ArrayKind.I32;
            int idx;
            if (!int.TryParse(txtArrayIndex.Text.Trim(), out idx)) { SetStatus("下标必须是整数", false); return; }
            string raw = txtArrayValue.Text.Trim();
            Mutate("统计数组元素", delegate(IMemory m, Chain c)
            {
                ulong arr;
                int len;
                string err;
                if (!Data.ReadArray(m, c, ArrayOffset(field), kind, out arr, out len, out err)) return err;
                if (!Limits.I32(idx, 0, Cfg.MaxArrayElems - 1)) return "下标需在 0.." + (Cfg.MaxArrayElems - 1);
                if (idx >= len) return "下标越界（数组长度 " + len + "）";
                ulong ea = arr + (ulong)Off.ArrayData + (ulong)(idx * (int)kind);
                if (kind == ArrayKind.F64)
                {
                    double d;
                    if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return "数值必须是数字";
                    if (!Limits.F64(d, -1e15, 1e15)) return "数值超出范围";
                    if (!WriteF64Safe(m, ea, d)) return "写入校验失败";
                }
                else
                {
                    int v;
                    if (!int.TryParse(raw, out v)) return "数值必须是整数";
                    if (!Limits.I32(v, -1000000000, 1000000000)) return "数值超出范围";
                    if (!WriteI32Safe(m, ea, v)) return "写入校验失败";
                }
                return null;
            });
        }

        // ------------------------------------------------------------ 备份
        private void DoBackup()
        {
            string dir = null;
            string[] cands = new string[] {
                Path.Combine(gameDir, "SaveData"),
                Path.Combine(Path.GetDirectoryName(gameDir), "SaveData")
            };
            foreach (string c in cands)
            {
                try { if (Directory.Exists(c) && Directory.GetFiles(c, "*.sav").Length > 0) { dir = c; break; } }
                catch { }
            }
            if (dir == null) dir = ScanLocalLow();
            if (dir == null)
            {
                FolderBrowserDialog fb = new FolderBrowserDialog();
                fb.Description = "未自动找到 SaveData 目录（*.sav），请手动选择";
                if (fb.ShowDialog(this) != DialogResult.OK) { SetStatus("已取消备份", false); return; }
                dir = fb.SelectedPath;
            }
            try
            {
                string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
                string outDir = Path.Combine(baseDir, "trainer", "backups", stamp);
                Directory.CreateDirectory(outDir);
                int n = 0;
                foreach (string f in Directory.GetFiles(dir, "*.sav"))
                {
                    File.Copy(f, Path.Combine(outDir, Path.GetFileName(f)), true);
                    n++;
                }
                SetStatus("已备份 " + n + " 个存档到 trainer\\backups\\" + stamp + "（原文件未改动）", true);
            }
            catch (Exception ex) { SetStatus("备份失败：" + ex.Message, false); }
        }

        private static string ScanLocalLow()
        {
            try
            {
                string ll = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "..", "LocalLow");
                if (!Directory.Exists(ll)) return null;
                foreach (string d1 in Directory.GetDirectories(ll))
                {
                    foreach (string d2 in Directory.GetDirectories(d1))
                    {
                        foreach (string d3 in Directory.GetDirectories(d2))
                        {
                            if (string.Equals(Path.GetFileName(d3), "SaveData", StringComparison.OrdinalIgnoreCase)
                                && Directory.GetFiles(d3, "*.sav").Length > 0) return d3;
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        // ------------------------------------------------------------ 刷新显示
        private void ClearDynamic()
        {
            if (lvItems != null) lvItems.Items.Clear();
            if (lvQuests != null) lvQuests.Items.Clear();
            if (lvDress != null) lvDress.Items.Clear();
            ovGold.Text = "—"; ovSperm.Text = "—"; ovDate.Text = "—"; ovLevel.Text = "—";
            ovStam.Text = "—"; ovInv.Text = "—"; ovQuest.Text = "—"; ovDress.Text = "—";
            ovGoldSub.Text = ""; ovSpermSub.Text = ""; ovDateSub.Text = ""; ovLevelSub.Text = "";
            ovStamSub.Text = ""; ovInvSub.Text = ""; ovQuestSub.Text = ""; ovDressSub.Text = "";
            ovConn.Text = "未连接：请先运行游戏并载入存档";
            ovChar.Text = "";
            lblWearInfo.Text = "—";
            lblCharStam.Text = "—";
            lblArousedInfo.Text = "—";
            lblFlags.Text = "";
            lblEnding.Text = "说明：待选结局 / 结局位属于存档一致性配对字段，本工具不提供修改，避免存档损坏。";
            lblInvInfo.Text = "";
            lblQuestInfo.Text = "";
            lblDressInfo.Text = "";
            lblArrayInfo.Text = "";
        }

        private static void SetIfIdle(TextBox t, string v)
        {
            if (t != null && !t.Focused && t.Text != v) t.Text = v;
        }

        private void UpdateDynamic(Snapshot s)
        {
            if (!s.SaveLoaded)
            {
                ClearDynamic();
                ovConn.Text = "已连接 PID " + s.Pid + "，等待游戏载入存档…";
                return;
            }
            SetIfIdle(txtGold, s.Gold.ToString(CultureInfo.InvariantCulture));
            SetIfIdle(txtSperm, s.Sperm.ToString("0.###", CultureInfo.InvariantCulture));
            SetIfIdle(txtDate, s.Date.ToString(CultureInfo.InvariantCulture));
            SetIfIdle(txtMaxStam, s.MaxStamina.ToString("0.###", CultureInfo.InvariantCulture));
            if (!cboDiff.Focused && cboDiff.SelectedIndex != s.Difficulty && Limits.I32(s.Difficulty, 0, 2)) cboDiff.SelectedIndex = s.Difficulty;
            if (!cboBody.Focused && cboBody.SelectedIndex != s.BodySize && Limits.I32(s.BodySize, 0, 4)) cboBody.SelectedIndex = s.BodySize;

            ovGold.Text = s.Gold.ToString("N0", CultureInfo.InvariantCulture);
            ovSperm.Text = s.Sperm.ToString("0.###", CultureInfo.InvariantCulture);
            ovDate.Text = s.Date.ToString(CultureInfo.InvariantCulture);
            ovLevel.Text = s.Level.ToString(CultureInfo.InvariantCulture);
            ovInv.Text = s.InvCount.ToString(CultureInfo.InvariantCulture);
            ovQuest.Text = s.QuestCount.ToString(CultureInfo.InvariantCulture);
            ovGoldSub.Text = "难度 " + Labels.DiffName(s.Difficulty) + " · 体型 " + Labels.BodyName(s.BodySize);
            ovSpermSub.Text = "收集资源（double）";
            ovDateSub.Text = s.InfinitePlay ? "无限游戏已开启" : "第 " + s.Date + " 天";
            ovLevelSub.Text = "由任务进度推导 · NG+" + s.NewGamePlus;
            ovInvSub.Text = "含全部容器的既有物品";
            ovQuestSub.Text = "成就 " + s.Achievements;
            if (s.MainCharPresent)
            {
                ovStam.Text = s.CharStamina.ToString("0.#", CultureInfo.InvariantCulture);
                ovStamSub.Text = "最大 " + s.CharMaxStamina.ToString("0.#", CultureInfo.InvariantCulture) + "（角色在地图中）";
                lblCharStam.Text = "当前耐力 / 最大耐力：" + s.CharStamina.ToString("0.##", CultureInfo.InvariantCulture) + " / " + s.CharMaxStamina.ToString("0.##", CultureInfo.InvariantCulture);
            }
            else
            {
                ovStam.Text = "—";
                ovStamSub.Text = "角色未进入地图";
                lblCharStam.Text = "角色状态：未进入地图（全耐力锁暂不可用）";
            }
            lblArousedInfo.Text = "兴奋度等级（DataHolder）：" + s.Aroused + " · 存档最大耐力 " + s.MaxStamina.ToString("0.##", CultureInfo.InvariantCulture);

            int[] owned = DressCatalog.OwnedPieces(s.Items);
            int ownN = DressCatalog.OwnedSeriesCount(owned);
            int trackN = DressCatalog.TrackableSeriesCount();
            ovDress.Text = ownN + " / " + trackN;
            ovDressSub.Text = "可识别系列（图鉴共 " + DressCatalog.All.Length + " 系列）";

            ovConn.Text = "PID " + s.Pid + " · Holder 0x" + s.Holder.ToString("X") + " · Save 0x" + s.Save.ToString("X")
                + "\nSHA256 校验通过 · 轮询 " + Cfg.PollMs + "ms · 背包 " + s.InvCount + " · 任务 " + s.QuestCount;

            // 当前穿搭
            List<string> wear = new List<string>();
            HashSet<int> seen = new HashSet<int>();
            if (s.Items != null)
            {
                foreach (ItemView it in s.Items)
                {
                    if (it.State != 2) continue;
                    if (!seen.Add(it.DbId)) continue;
                    wear.Add(it.Name.Length == 0 ? ("#" + it.DbId) : it.Name);
                }
            }
            ovChar.Text = wear.Count > 0
                ? "已装备 " + wear.Count + " 件（去重后）"
                : "当前无装备记录";
            lblWearInfo.Text = wear.Count > 0 ? JoinStr(wear, "\n") : "（未检测到已装备物品）";

            SetIfIdle(txtAchievements, s.Achievements.ToString(CultureInfo.InvariantCulture));
            SetIfIdle(txtNgPlus, s.NewGamePlus.ToString(CultureInfo.InvariantCulture));
            SetIfIdle(txtArrests, s.Arrests.ToString(CultureInfo.InvariantCulture));
            SetIfIdle(txtBestDays, s.BestClearDays.ToString(CultureInfo.InvariantCulture));
            SetIfIdle(txtBestOuting, s.BestOuting.ToString(CultureInfo.InvariantCulture));
            SetIfIdle(txtTutorial, s.TutorialLevel.ToString(CultureInfo.InvariantCulture));
            SetIfIdle(txtAroused, s.Aroused.ToString(CultureInfo.InvariantCulture));
            SetIfIdle(txtFetish, s.FetishMask.ToString("X", CultureInfo.InvariantCulture));
            SetIfIdle(txtGuides, s.SeenGuides.ToString("X", CultureInfo.InvariantCulture));
            SetIfIdle(txtSexMask, s.SexNpcMask.ToString("X", CultureInfo.InvariantCulture));
            SetIfIdle(txtChased, s.ChasedMask.ToString("X", CultureInfo.InvariantCulture));
            SetIfIdle(txtNoise, s.NoiseMask.ToString("X", CultureInfo.InvariantCulture));
            SetIfIdle(txtUndetected, s.UndetectedMask.ToString("X", CultureInfo.InvariantCulture));
            SetIfIdle(txtEnding, s.EndingMask.ToString("X", CultureInfo.InvariantCulture));

            SyncChecks(this, s);
            lblFlags.Text = "只读标记：分组背包=" + (s.GroupedInv ? "是" : "否")
                + "，本局换装过=" + (s.OutfitChanged ? "是" : "否")
                + "，成就历史已初始化=" + (s.AchInit ? "是" : "否")
                + "，被逮捕=" + s.Arrests + "，待选结局=" + (s.PendingChoice ? "是" : "否");

            RefreshIfStale(s);
        }

        private int lastInvGen = -1, lastQuestGen = -1;

        private void RefreshIfStale(Snapshot s)
        {
            int key = s.Pid ^ (int)(s.Save & 0xFFFF) ^ (int)(s.Holder & 0xFFFF);
            if (key != lastInvGen && lvItems != null && lvItems.Items.Count == 0) { RefreshInventory(false); lastInvGen = key; }
            if (key != lastQuestGen && lvQuests != null && lvQuests.Items.Count == 0) { RefreshQuests(false); lastQuestGen = key; }
            if (key != lastDressGen && lvDress != null && lvDress.Items.Count == 0) { RefreshWardrobe(s.Items); lastDressGen = key; }
        }

        private void SyncChecks(Control root, Snapshot s)
        {
            foreach (Control c in root.Controls)
            {
                CheckBox cb = c as CheckBox;
                if (cb == null) { if (c.Controls.Count > 0) SyncChecks(c, s); continue; }
                if (cb == chkSpermLock || cb == chkStamLock || cb == chkDateLock) continue;
                if (cb.Text.IndexOf("无限游戏", StringComparison.Ordinal) == 0)
                {
                    if (cb.Checked != s.InfinitePlay)
                    {
                        suppressCheck = true;
                        cb.Checked = s.InfinitePlay;
                        suppressCheck = false;
                    }
                }
                else if (cb.Text.IndexOf("兔子警察", StringComparison.Ordinal) == 0)
                {
                    if (cb.Checked != s.BunnyPolice)
                    {
                        suppressCheck = true;
                        cb.Checked = s.BunnyPolice;
                        suppressCheck = false;
                    }
                }
            }
        }

        private bool suppressCheck;

        public void LoadPreviewData()
        {
            Snapshot s = new Snapshot();
            s.Connected = true;
            s.Pid = 40836;
            s.HashOk = true;
            s.SaveLoaded = true;
            s.Holder = 0x747F9D20;
            s.Save = 0x66116780;
            s.Gold = 9935757;
            s.Sperm = 0;
            s.Date = 8;
            s.Difficulty = 1;
            s.BodySize = 1;
            s.Preset = 0;
            s.Level = 1;
            s.Achievements = 12;
            s.MaxStamina = 100f;
            s.MainCharPresent = true;
            s.CharStamina = 61f;
            s.CharMaxStamina = 100f;
            s.Aroused = 2;
            s.InfinitePlay = false;
            s.InvCount = 204;
            s.QuestCount = 98;
            List<ItemView> items = new List<ItemView>();
            string[,] defs = new string[,] {
                {"Lottie Dress","44","3","0"},
                {"Sailor Swimsuit's Shirt","8","0","2"},
                {"Evergrace Bunny Hat","58","0","2"},
                {"Romance China Arm","154","0","2"},
                {"Nerldy baby long hair","32","0","2"},
                {"魔力药水","101","1","0"},
                {"面包","102","1","0"},
                {"Goth Twintail","28","5","0"},
                {"Neko BoB","31","5","0"},
                {"Sport Swimsuit (Pulled down)","361","0","2"}
            };
            int[] dbids = new int[] { 44, 8, 58, 154, 32, 101, 102, 28, 31, 361 };
            for (int i = 0; i < defs.GetLength(0); i++)
            {
                ItemView it = new ItemView();
                it.Name = defs[i, 0];
                it.DbId = dbids[i];
                it.InvType = int.Parse(defs[i, 2], CultureInfo.InvariantCulture);
                it.State = int.Parse(defs[i, 3], CultureInfo.InvariantCulture);
                it.Count = 1; it.MaxCount = 1; it.ItemType = 1; it.Index = i;
                items.Add(it);
            }
            s.Items = items;
            s.Quests = new List<QuestView>();
            snap = s;
            SetStatus("已连接 PID 40836 · 已载入存档 · 预览", true);
            UpdateDynamic(s);
            RefreshInventoryPreview(items);
            RefreshWardrobe(items);

            lvQuests.Items.Clear();
            string[,] qs = new string[,] {
                {"初次相遇", "1001", "1", "3", "5"},
                {"收集素材", "1002", "1", "0", "10"},
                {"村中传闻", "1003", "2", "1", "1"},
                {"夜间巡逻", "1004", "3", "2", "6"}
            };
            for (int i = 0; i < qs.GetLength(0); i++)
            {
                ListViewItem li = new ListViewItem(new string[] { qs[i, 0], qs[i, 1], qs[i, 2], qs[i, 3], qs[i, 4] });
                lvQuests.Items.Add(li);
            }
            lblQuestInfo.Text = "共 4 条任务记录；写满进度后仍需回游戏正常提交才能领取奖励。";
            lblArrayInfo.Text = "数组长度 24，上限 512，元素类型 int32。";
        }

        private void RefreshInventoryPreview(List<ItemView> items)
        {
            lvItems.Items.Clear();
            foreach (ItemView it in items)
            {
                ListViewItem li = new ListViewItem(new string[] {
                    it.Name,
                    it.DbId.ToString(CultureInfo.InvariantCulture),
                    it.ItemType.ToString(CultureInfo.InvariantCulture),
                    Labels.InvTypeName(it.InvType),
                    it.Count.ToString(CultureInfo.InvariantCulture),
                    it.MaxCount.ToString(CultureInfo.InvariantCulture),
                    Labels.StateName(it.State),
                    "0,0"
                });
                li.ForeColor = it.State == 2 ? Theme.Accent : Theme.Text;
                lvItems.Items.Add(li);
            }
            lblInvInfo.Text = "共 " + items.Count + " 件物品（预览数据）。粉色行 = 当前已装备。";
        }
    }

    // ================================================================ 入口
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            string mode = args.Length > 0 ? args[0].ToLowerInvariant() : "";
            if (mode == "--probe") { Con.EnsureStdout(); return Probe(); }
            if (mode == "--selftest") { Con.EnsureStdout(); return SelfTest(); }
            if (mode == "--preview") { Con.EnsureStdout(); return Preview(); }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            MainForm f = new MainForm();
            f.StartMonitoring();
            Application.Run(f);
            return 0;
        }

        private static string J(string s)
        {
            if (s == null) return "null";
            StringBuilder sb = new StringBuilder("\"");
            foreach (char c in s)
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c == '\n') sb.Append("\\n");
                else if (c == '\r') sb.Append("\\r");
                else if (c < 32) sb.Append('?');
                else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }

        private static int Probe()
        {
            string gameDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            StringBuilder o = new StringBuilder();
            o.Append("{\"mode\":\"probe\",");
            Process target = null;
            try
            {
                Process[] ps = Process.GetProcessesByName(Cfg.ProcessName);
                foreach (Process p in ps)
                {
                    try
                    {
                        string d = Path.GetDirectoryName(p.MainModule.FileName).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                        if (string.Equals(d, gameDir, StringComparison.OrdinalIgnoreCase)) { target = p; break; }
                    }
                    catch { }
                }
            }
            catch { }
            if (target == null)
            {
                o.Append("\"attached\":false,\"error\":\"未找到游戏进程\"}");
                Console.WriteLine(o.ToString());
                return 2;
            }
            o.Append("\"attached\":true,\"pid\":").Append(target.Id).Append(',');
            o.Append("\"exe\":").Append(J(target.MainModule.FileName)).Append(',');

            ulong ga;
            if (!FindGa(target, out ga))
            {
                o.Append("\"error\":\"未找到 GameAssembly.dll\"}");
                Console.WriteLine(o.ToString());
                return 3;
            }
            o.Append("\"gameAssemblyBase\":\"0x").Append(ga.ToString("X")).Append("\",");

            string hp = Path.Combine(gameDir, Cfg.AssemblyName);
            bool sizeOk = false;
            string hash = "";
            try
            {
                FileInfo fi = new FileInfo(hp);
                sizeOk = fi.Exists && fi.Length == Cfg.AssemblySize;
                using (SHA256 sha = SHA256.Create())
                using (FileStream fs = File.OpenRead(hp))
                {
                    byte[] h = sha.ComputeHash(fs);
                    StringBuilder sb = new StringBuilder();
                    for (int i = 0; i < h.Length; i++) sb.Append(h[i].ToString("X2", CultureInfo.InvariantCulture));
                    hash = sb.ToString();
                }
            }
            catch { }
            bool hashOk = sizeOk && string.Equals(hash, Cfg.AssemblySha256, StringComparison.OrdinalIgnoreCase);
            o.Append("\"sizeOk\":").Append(sizeOk ? "true" : "false").Append(',');
            o.Append("\"hashOk\":").Append(hashOk ? "true" : "false").Append(',');

            using (ProcessMemory m = new ProcessMemory(target.Id))
            {
                Chain c;
                string err;
                if (!Resolver.Resolve(m, ga, out c, out err))
                {
                    o.Append("\"resolved\":false,\"error\":").Append(J(err)).Append('}');
                    Console.WriteLine(o.ToString());
                    return 4;
                }
                o.Append("\"resolved\":true,");
                o.Append("\"holder\":\"0x").Append(c.Holder.ToString("X")).Append("\",");
                o.Append("\"holderClass\":").Append(J(c.HolderClass)).Append(',');
                o.Append("\"save\":\"0x").Append(c.Save.ToString("X")).Append("\",");
                o.Append("\"saveClass\":").Append(J(c.SaveClass)).Append(',');
                if (c.Save == 0)
                {
                    o.Append("\"saveLoaded\":false}");
                    Console.WriteLine(o.ToString());
                    return 0;
                }
                Snapshot s = new Snapshot();
                s.SaveLoaded = true;
                Data.ReadBasics(m, c, s);
                int[] owned = DressCatalog.OwnedPieces(s.Items);
                o.Append("\"saveLoaded\":true,");
                o.Append("\"gold\":").Append(s.Gold).Append(',');
                o.Append("\"currentDate\":").Append(s.Date).Append(',');
                o.Append("\"difficulty\":").Append(s.Difficulty).Append(',');
                o.Append("\"currentLevel\":").Append(s.Level).Append(',');
                o.Append("\"currentResource\":").Append(s.Sperm.ToString("R", CultureInfo.InvariantCulture)).Append(',');
                o.Append("\"maxStamina\":").Append(s.MaxStamina.ToString("R", CultureInfo.InvariantCulture)).Append(',');
                o.Append("\"infinitePlay\":").Append(s.InfinitePlay ? "true" : "false").Append(',');
                o.Append("\"bodySize\":").Append(s.BodySize).Append(',');
                o.Append("\"inventoryCount\":").Append(s.InvCount).Append(',');
                o.Append("\"questCount\":").Append(s.QuestCount).Append(',');
                o.Append("\"dressOwnedSeries\":").Append(DressCatalog.OwnedSeriesCount(owned)).Append(',');
                o.Append("\"dressTrackableSeries\":").Append(DressCatalog.TrackableSeriesCount()).Append(',');
                o.Append("\"dressCatalogTotal\":").Append(DressCatalog.All.Length).Append(',');
                o.Append("\"mainCharPresent\":").Append(s.MainCharPresent ? "true" : "false").Append(',');
                o.Append("\"charStamina\":").Append(s.CharStamina.ToString("R", CultureInfo.InvariantCulture)).Append(',');
                o.Append("\"charMaxStamina\":").Append(s.CharMaxStamina.ToString("R", CultureInfo.InvariantCulture)).Append(',');
                o.Append("\"writeHandleOpened\":false");
                o.Append('}');
                Console.WriteLine(o.ToString());
            }
            return 0;
        }

        private static bool FindGa(Process p, out ulong ga)
        {
            ga = 0;
            try
            {
                foreach (ProcessModule mod in p.Modules)
                {
                    if (string.Equals(mod.ModuleName, Cfg.AssemblyName, StringComparison.OrdinalIgnoreCase))
                    {
                        ga = unchecked((ulong)mod.BaseAddress.ToInt64());
                        return ga != 0;
                    }
                }
            }
            catch { }
            return false;
        }

        private static int Preview()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            MainForm f = new MainForm();
            f.LoadPreviewData();
            f.Show();
            Application.DoEvents();
            System.Threading.Thread.Sleep(400);
            Application.DoEvents();
            string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "trainer");
            Directory.CreateDirectory(dir);
            string[] keys = new string[] { "overview", "resource", "inventory", "quest", "wardrobe", "advanced" };
            foreach (string key in keys)
            {
                f.ShowPagePublic(key);
                Application.DoEvents();
                System.Threading.Thread.Sleep(200);
                Application.DoEvents();
                string outPath = Path.Combine(dir, "preview-" + key + ".png");
                using (Bitmap bmp = new Bitmap(f.Width, f.Height))
                {
                    f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height));
                    bmp.Save(outPath, ImageFormat.Png);
                }
                Console.WriteLine("preview -> " + outPath);
            }
            f.ShowPagePublic("overview");
            Application.DoEvents();
            using (Bitmap bmp = new Bitmap(f.Width, f.Height))
            {
                f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height));
                bmp.Save(Path.Combine(dir, "preview.png"), ImageFormat.Png);
            }
            UiLint(f, dir);
            f.Close();
            return 0;
        }

        /// <summary>诊断：固定宽度控件的文字溢出与负坐标检查，写入 ui-lint.txt。</summary>
        private static void UiLint(Form f, string dir)
        {
            List<string> issues = new List<string>();
            WalkLint(f, issues, 0, 0);
            string outPath = Path.Combine(dir, "ui-lint.txt");
            File.WriteAllLines(outPath, issues.ToArray());
            Console.WriteLine("lint issues: " + issues.Count + " -> " + outPath);
        }

        private static void WalkLint(Control root, List<string> issues, int ox, int oy)
        {
            foreach (Control c in root.Controls)
            {
                int ax = ox + c.Left, ay = oy + c.Top;
                if (ax < -2 || ay < -2)
                    issues.Add("负坐标: " + c.GetType().Name + " '" + SafeText(c) + "' @(" + ax + "," + ay + ")");
                Label l = c as Label;
                if (l != null && !l.AutoSize && !string.IsNullOrEmpty(l.Text))
                {
                    Size sz = TextRenderer.MeasureText(l.Text, l.Font);
                    if (sz.Width > l.Width + 2)
                        issues.Add("文字溢出: '" + l.Text + "' 需 " + sz.Width + "px > 宽 " + l.Width + "px");
                }
                if (c.Controls.Count > 0) WalkLint(c, issues, ax, ay);
            }
        }

        private static string SafeText(Control c)
        {
            string t = c.Text;
            if (t == null) return "";
            return t.Length > 24 ? t.Substring(0, 24) : t;
        }

        // ------------------------------------------------------------ selftest
        private static int SelfTest()
        {
            int fails = 0;
            List<string> log = new List<string>();

            Action<string, bool> chk = delegate(string name, bool ok)
            {
                log.Add((ok ? "PASS " : "FAIL ") + name);
                if (!ok) fails++;
            };

            // ---- 边界与地址校验 ----
            chk("地址下界拒绝", !Addr.Valid(0x10));
            chk("地址上界拒绝", !Addr.Valid(0x7FFFFFFFFFFFUL));
            chk("长度溢出拒绝", !Addr.Range(Addr.Max - 3, 8));
            chk("标量范围：金币上界", !Limits.I32(2000000001, 0, 2000000000) && Limits.I32(2000000000, 0, 2000000000));
            chk("标量范围：日期非零", !Limits.I32(0, 1, 1000000));
            chk("标量范围：NaN/Inf 拒绝", !Limits.F64(double.NaN, 0, 1) && !Limits.F32(float.PositiveInfinity, 0f, 1f));
            chk("枚举越界拒绝", !Limits.I32(3, 0, 2) && !Limits.I32(5, 0, 4));

            // ---- 服装目录 ----
            chk("服装目录共 36 系列", DressCatalog.All.Length == 36);
            int trackable = DressCatalog.TrackableSeriesCount();
            chk("全部 36 系列可识别", trackable == 36);
            bool matchOk = true, colorOk = true;
            for (int i = 0; i < DressCatalog.All.Length; i++)
            {
                DressCatalog.Series se = DressCatalog.All[i];
                if (se.Key == null || se.Zh == null || se.Colors == null || se.Parts == null) matchOk = false;
                for (int k = 0; k < se.Colors.Length; k++)
                    if (se.Colors[k] == null || se.Colors[k].Length == 0 || se.Colors[k][0] != 'C') colorOk = false;
            }
            chk("目录字段完整", matchOk);
            chk("配色编码合法", colorOk);
            List<ItemView> probeItems = new List<ItemView>();
            ItemView a = new ItemView(); a.Name = "Lottie Dress"; a.DbId = 44; a.InvType = 3;
            ItemView b2 = new ItemView(); b2.Name = "Lottie Hat"; b2.DbId = 39; b2.InvType = 3;
            ItemView c3 = new ItemView(); c3.Name = "Romance China Dress"; c3.DbId = 62; c3.InvType = 3;
            ItemView d4 = new ItemView(); d4.Name = "Sailor Swimsuit's Shirt"; d4.DbId = 8; d4.InvType = 0; d4.State = 2;
            ItemView e5 = new ItemView(); e5.Name = "Sailor Swimsuit's Shirt"; e5.DbId = 160; e5.InvType = 3;
            ItemView f6 = new ItemView(); f6.Name = "Lottie Dress"; f6.DbId = 44; f6.InvType = 4; // 商店容器不计
            probeItems.Add(a); probeItems.Add(b2); probeItems.Add(c3); probeItems.Add(d4); probeItems.Add(e5); probeItems.Add(f6);
            int[] own = DressCatalog.OwnedPieces(probeItems);
            int lottieIdx = -1, rcIdx = -1, smIdx = -1, asmoIdx = -1;
            for (int i = 0; i < DressCatalog.All.Length; i++)
            {
                if (DressCatalog.All[i].Key == "Lottie") lottieIdx = i;
                if (DressCatalog.All[i].Key == "RomanceChina") rcIdx = i;
                if (DressCatalog.All[i].Key == "SailorMizu") smIdx = i;
                if (DressCatalog.All[i].Key == "Asmodeus") asmoIdx = i;
            }
            chk("拥有判定：Lottie=2 件（商店不计）", lottieIdx >= 0 && own[lottieIdx] == 2);
            chk("拥有判定：RomanceChina=1", rcIdx >= 0 && own[rcIdx] == 1);
            chk("拥有判定：SailorMizu=2", smIdx >= 0 && own[smIdx] == 2);
            chk("拥有判定：未拥有 Asmodeus=0", asmoIdx >= 0 && own[asmoIdx] == 0);
            chk("拥有系列计数=3", DressCatalog.OwnedSeriesCount(own) == 3);

            // ---- 合成内存布局 ----
            MockMemory m = new MockMemory(0x4000000);
            ulong ga = MockMemory.Base;
            ulong kDataHolder = 0x10000100, kSave = 0x10000120, kInv = 0x10000140, kList = 0x10000160;
            ulong kItem = 0x10000180, kString = 0x100001A0, kArr = 0x100001C0, kQuest = 0x100001E0;
            // 每个类型描述符/方法链节点/对象/列表/数组/字符串都占独立 0x1000 区间，互不重叠
            ulong method = 0x10001000, klass = 0x10002000, rgctx = 0x10003000, sing = 0x10004000, stat = 0x10005000;
            ulong holder = 0x10010000, save = 0x10020000;
            ulong inv = 0x10030000, list = 0x10031000, array = 0x10032000;
            ulong item0 = 0x10033000, item1 = 0x10034000;
            ulong sName = 0x10036000;
            ulong qArr = 0x10040000, quest0 = 0x10041000, qName = 0x10043000;

            WStr(m, 0x10000010, "DataHolder");
            WStr(m, 0x10000020, "SaveData");
            WStr(m, 0x10000030, "InventoryData");
            WStr(m, 0x10000040, "List`1");
            WStr(m, 0x10000050, "ItemData");
            WStr(m, 0x10000060, "String");
            WStr(m, 0x10000068, "QuestData");
            WStr(m, 0x10000078, "Array");
            W64(m, kDataHolder + 0x10, 0x10000010);
            W64(m, kSave + 0x10, 0x10000020);
            W64(m, kInv + 0x10, 0x10000030);
            W64(m, kList + 0x10, 0x10000040);
            W64(m, kItem + 0x10, 0x10000050);
            W64(m, kString + 0x10, 0x10000060);
            W64(m, kQuest + 0x10, 0x10000068);
            W64(m, kArr + 0x10, 0x10000078);

            W64(m, ga + (ulong)Off.GaMethod, method);
            W64(m, method + 0x20, klass);
            W64(m, klass + 0xC0, rgctx);
            W64(m, rgctx + 0x10, sing);
            W64(m, sing + 0xB8, stat);
            W64(m, stat, holder);
            W64(m, holder, kDataHolder);
            W64(m, holder + (ulong)Off.HolderSave, save);
            W64(m, save, kSave);

            W32(m, save + Off.Gold, 9935757);
            W32(m, save + Off.CurrentDate, 8);
            W32(m, save + Off.Difficulty, 1);
            W32(m, save + Off.CurrentLevel, 1);
            W64(m, save + Off.CurrentSperm, (ulong)0);
            WF32(m, save + Off.MaxStamina, 100f);
            WBool(m, save + Off.InfinitePlay, false);
            W64(m, save + Off.Inventory, inv);
            W64(m, inv, kInv);
            W64(m, inv + 0x10, list);
            W64(m, list, kList);
            W64(m, list + 0x10, array);
            W32(m, list + 0x18, 2);
            W64(m, array, kArr);
            W64(m, array + 0x18, 4UL);
            W64(m, array + 0x20, item0);
            W64(m, array + 0x28, item1);
            WIl2Str(m, sName, "Lillia");
            W64(m, item0, kItem);
            W32(m, item0 + Off.ItemDatabaseId, 101);
            W64(m, item0 + Off.ItemRawName, sName);
            W32(m, item0 + Off.ItemType, 1);
            W32(m, item0 + Off.ItemMaxCount, 9);
            W32(m, item0 + Off.ItemState, 0);
            W32(m, item0 + Off.ItemInvType, 10);
            W32(m, item0 + Off.ItemCount, 5);
            W64(m, item1, kItem);
            W32(m, item1 + Off.ItemDatabaseId, 102);
            W32(m, item1 + Off.ItemMaxCount, 5);
            W32(m, item1 + Off.ItemCount, 3);
            W32(m, item1 + Off.ItemInvType, 1);

            W64(m, save + Off.QuestList, qArr);
            W64(m, qArr, kArr);
            W64(m, qArr + 0x18, 1UL);
            W64(m, qArr + 0x20, quest0);
            W64(m, quest0, kQuest);
            W32(m, quest0 + Off.QuestId, 1001);
            W32(m, quest0 + Off.QuestLevel, 2);
            W64(m, quest0 + Off.QuestRawName, qName);
            W32(m, quest0 + Off.QuestMaxCount, 5);
            W32(m, quest0 + Off.QuestCurrent, 3);
            WIl2Str(m, qName, "初次相遇");

            // ---- 链路解析 ----
            Chain c;
            string err;
            bool resolved = Resolver.Resolve(m, ga, out c, out err);
            chk("链路解析成功", resolved);
            if (resolved)
            {
                chk("holder 地址匹配", c.Holder == holder);
                chk("save 地址匹配", c.Save == save);
                chk("holder 类型名 = DataHolder", c.HolderClass == "DataHolder");
                chk("save 类型名 = SaveData", c.SaveClass == "SaveData");

                Snapshot s = new Snapshot();
                s.SaveLoaded = true;
                Data.ReadBasics(m, c, s);
                chk("读取金币 9935757", s.Gold == 9935757);
                chk("读取日期 8", s.Date == 8);
                chk("读取难度 1", s.Difficulty == 1);
                chk("读取等级 1", s.Level == 1);
                chk("读取资源 0", Math.Abs(s.Sperm) < 1e-9);
                chk("读取最大耐力 100", Math.Abs(s.MaxStamina - 100f) < 1e-6);
                chk("读取 infinitePlay false", s.InfinitePlay == false);
                chk("未进入地图时无角色", s.MainCharPresent == false);
                chk("背包条目 2", s.InvCount == 2);
                chk("任务条目 1", s.QuestCount == 1);
                chk("快照含物品明细", s.Items != null && s.Items.Count == 2 && s.Items[0].Name == "Lillia");
            }

            // ---- IL2CPP 字符串 ----
            chk("ASCII 类名读取", M.ClassName(m, holder) == "DataHolder");
            chk("UTF16 字符串读取", M.Il2CppString(m, sName) == "Lillia");
            string cjk = M.Il2CppString(m, qName);
            chk("中文 UTF16 字符串读取", cjk == "初次相遇");

            // ---- 写入与回读 ----
            bool w1 = M.WriteI32(m, save + Off.Gold, 123456);
            int back;
            chk("写入金币成功", w1 && M.ReadI32(m, save + Off.Gold, out back) && back == 123456);
            bool w2 = M.WriteF32(m, save + Off.MaxStamina, 250.5f);
            float fb;
            chk("写入浮点成功", w2 && M.ReadF32(m, save + Off.MaxStamina, out fb) && Math.Abs(fb - 250.5f) < 1e-4);
            bool w3 = M.WriteF64(m, save + Off.CurrentSperm, 42.75d);
            double db;
            chk("写入 double 成功", w3 && M.ReadF64(m, save + Off.CurrentSperm, out db) && Math.Abs(db - 42.75d) < 1e-9);
            bool w4 = M.WriteBool(m, save + Off.InfinitePlay, true);
            bool bb;
            chk("写入 bool 成功", w4 && M.ReadBool(m, save + Off.InfinitePlay, out bb) && bb);
            bool w5 = M.WriteI32(m, item0 + Off.ItemInvType, 1);
            int ib;
            chk("写入容器字段成功（移动到背包）", w5 && M.ReadI32(m, item0 + Off.ItemInvType, out ib) && ib == 1);

            // ---- 列表 / 数组解析与边界 ----
            List<ItemView> items;
            string ierr;
            bool invOk = Data.ReadInventory(m, c, out items, out ierr);
            chk("背包解析成功", invOk);
            if (invOk)
            {
                chk("背包数量 2", items.Count == 2);
                chk("物品0 名称", items.Count > 0 && items[0].Name == "Lillia");
                chk("物品0 数据库ID", items.Count > 0 && items[0].DbId == 101);
                chk("物品0 容器=背包(1)（移动写入后）", items.Count > 0 && items[0].InvType == 1);
                chk("物品0 数量 5", items.Count > 0 && items[0].Count == 5);
                chk("物品1 无名称不报错", items.Count > 1 && items[1].Name == "");
                chk("物品1 上限 5", items.Count > 1 && items[1].MaxCount == 5);
            }
            W32(m, list + 0x18, 9999);
            List<ItemView> bad;
            string berr;
            chk("列表长度越界被拒绝", !Data.ReadInventory(m, c, out bad, out berr));
            W32(m, list + 0x18, 5);
            chk("列表长度>数组长度被拒绝", !Data.ReadInventory(m, c, out bad, out berr));
            W32(m, list + 0x18, 2);

            List<QuestView> qs;
            string qerr;
            bool qOk = Data.ReadQuests(m, c, out qs, out qerr);
            chk("任务解析成功", qOk);
            if (qOk)
            {
                chk("任务数量 1", qs.Count == 1);
                chk("任务 ID 1001", qs.Count > 0 && qs[0].Id == 1001);
                chk("任务名称中文", qs.Count > 0 && qs[0].Name == "初次相遇");
                chk("任务上限 5 / 进度 3", qs.Count > 0 && qs[0].MaxCount == 5 && qs[0].Current == 3);
            }
            W64(m, qArr + 0x18, 900UL);
            chk("任务数组越界被拒绝", !Data.ReadQuests(m, c, out qs, out qerr));
            W64(m, qArr + 0x18, 1UL);

            // ---- 统计数组 ----
            W64(m, save + Off.D0, qArr);
            ulong gotArr;
            int gotLen;
            string aerr;
            chk("数组读取成功", Data.ReadArray(m, c, Off.D0, ArrayKind.I32, out gotArr, out gotLen, out aerr) && gotLen == 1);
            W64(m, qArr + 0x18, 4000UL);
            chk("数组长度越界被拒绝", !Data.ReadArray(m, c, Off.D0, ArrayKind.I32, out gotArr, out gotLen, out aerr));
            W64(m, qArr + 0x18, 1UL);

            // ---- 掩码解析 ----
            int mv;
            chk("十六进制掩码解析", TryParseMaskLocal("0x1F", out mv) && mv == 31);
            chk("纯十六进制掩码解析", TryParseMaskLocal("FF", out mv) && mv == 255);
            chk("非法掩码拒绝", !TryParseMaskLocal("zz", out mv));

            // ---- 只读边界：不存在的地址 ----
            byte[] tmp = new byte[4];
            chk("越界地址读取失败", !m.ReadBytes(0x0, tmp, 0, 4));
            chk("越界地址写入失败", !m.WriteBytes(0x0, tmp, 0, 4));

            // ---- 原生 P/Invoke 路径：本进程 AllocHGlobal 内存，绝不接触游戏 ----
            IntPtr native = System.Runtime.InteropServices.Marshal.AllocHGlobal(64);
            try
            {
                ulong np = unchecked((ulong)native.ToInt64());
                if (!Addr.Range(np, 64))
                {
                    log.Add("SKIP 原生 P/Invoke 自测：分配地址 " + np.ToString("X") + " 落在校验范围外");
                }
                else
                {
                    ProcessMemory pm = new ProcessMemory(System.Diagnostics.Process.GetCurrentProcess().Id);
                    try
                    {
                        byte[] pat = new byte[] { 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88 };
                        bool nw = pm.WriteBytes(np + 8UL, pat, 0, pat.Length);
                        chk("原生 WriteProcessMemory 成功", nw);
                        byte[] nback = new byte[8];
                        bool nr = pm.ReadBytes(np + 8UL, nback, 0, nback.Length);
                        chk("原生 ReadProcessMemory 回读一致",
                            nr && BitConverter.ToUInt64(nback, 0) == BitConverter.ToUInt64(pat, 0));
                        chk("原生写入句柄已打开", pm.CanWrite);
                        chk("越界原生地址读取被拒绝", !pm.ReadBytes(0x0UL, nback, 0, nback.Length));
                    }
                    finally { pm.Dispose(); }
                }
            }
            finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(native); }

            Console.WriteLine("=== selftest (合成内存，绝不接触真实游戏) ===");
            int shown = 0;
            foreach (string line in log)
            {
                if (line.StartsWith("FAIL", StringComparison.Ordinal) || shown < 6)
                {
                    Console.WriteLine(line);
                    shown++;
                }
            }
            Console.WriteLine("--- 共 " + log.Count + " 项，失败 " + fails + " 项 ---");
            return fails == 0 ? 0 : 1;
        }

        private static bool TryParseMaskLocal(string text, out int v)
        {
            v = 0;
            string t = text.Trim();
            if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) t = t.Substring(2);
            if (t.Length == 0) return false;
            return int.TryParse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v);
        }

        private static void WStr(IMemory m, ulong addr, string s)
        {
            byte[] b = Encoding.ASCII.GetBytes(s);
            byte[] all = new byte[b.Length + 1];
            Buffer.BlockCopy(b, 0, all, 0, b.Length);
            m.WriteBytes(addr, all, 0, all.Length);
        }

        private static void WIl2Str(IMemory m, ulong addr, string s)
        {
            byte[] kl = BitConverter.GetBytes(0x100001A0UL);
            m.WriteBytes(addr, kl, 0, 8);
            W32(m, addr + 0x10, s.Length);
            byte[] d = Encoding.Unicode.GetBytes(s);
            m.WriteBytes(addr + 0x14, d, 0, d.Length);
        }

        private static void W32(IMemory m, ulong addr, int v) { M.WriteI32(m, addr, v); }
        private static void W32(IMemory m, ulong addr, ulong v) { W64(m, addr, v); }
        private static void W64(IMemory m, ulong addr, ulong v)
        {
            byte[] b = BitConverter.GetBytes(v);
            m.WriteBytes(addr, b, 0, 8);
        }
        private static void WF32(IMemory m, ulong addr, float v) { M.WriteF32(m, addr, v); }
        private static void WBool(IMemory m, ulong addr, bool v) { M.WriteBool(m, addr, v); }
    }
}
