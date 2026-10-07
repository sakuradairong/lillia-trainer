using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace LilliaTrainer
{
    internal sealed class OutfitItem
    {
        public ItemView Item;
        public string Model, Equip, Color;
        public int Mask;
    }
    internal sealed class OutfitPlan
    {
        public readonly List<OutfitItem> Items = new List<OutfitItem>();
        public string Error;
        public int Skipped;
    }
    internal static class OutfitPlanner
    {
        public static string ColorKey(string s)
        {
            int n;
            return s != null && s.StartsWith("C", StringComparison.Ordinal)
                && int.TryParse(s.Substring(1), out n) ? "C" + n.ToString("00", CultureInfo.InvariantCulture) : "";
        }
        public static string ModelColor(string model)
        {
            Match m = Regex.Match(model ?? "", @"(?:_Color|_C)(\d{1,2})(?:_|$)", RegexOptions.IgnoreCase);
            return m.Success ? ColorKey("C" + m.Groups[1].Value) : "C01";
        }
        public static int EquipMask(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            int mask = 0;
            foreach (string part in s.Split('|'))
            {
                switch (part.Trim())
                {
                    case "Accessory1": mask |= 1; break;
                    case "Accessory2": mask |= 2; break;
                    case "Shoes": mask |= 4; break;
                    case "Panties": mask |= 8; break;
                    case "Bra": mask |= 16; break;
                    case "UnderDress": mask |= 32; break;
                    case "UpperDress": mask |= 64; break;
                    case "Stockings": mask |= 128; break;
                    case "Arm": mask |= 256; break;
                    case "HeadAccessory1": mask |= 512; break;
                    case "Hat": mask |= 2048; break;
                    case "Hair": mask |= 4096; break;
                    default: return 0;
                }
            }
            return mask;
        }
        private static int Bits(int x) { int n = 0; while (x != 0) { n += x & 1; x >>= 1; } return n; }
        private static bool Matches(ItemView it, DressCatalog.Series s)
        {
            if (it == null || string.IsNullOrEmpty(it.Name) || string.IsNullOrEmpty(s.Match)) return false;
            foreach (string word in s.Match.Split('|'))
                if (word.Length > 0 && it.Name.IndexOf(word, StringComparison.Ordinal) >= 0) return true;
            return false;
        }
        public static List<OutfitItem> Read(IMemory m, List<ItemView> items, DressCatalog.Series series)
        {
            List<OutfitItem> result = new List<OutfitItem>();
            if (items == null) return result;
            foreach (ItemView it in items)
            {
                if (it.ItemType != 1 || !Matches(it, series)) continue;
                string model = M.Il2CppString(m, M.PtrOr0(m, it.Ptr + 0x48));
                string equip = M.Il2CppString(m, M.PtrOr0(m, it.Ptr + 0x58));
                result.Add(new OutfitItem { Item = it, Model = model, Equip = equip,
                    Color = ModelColor(model), Mask = EquipMask(equip) });
            }
            return result;
        }
        public static OutfitPlan Build(List<OutfitItem> source, string color)
        {
            OutfitPlan plan = new OutfitPlan();
            List<OutfitItem> candidates = new List<OutfitItem>();
            foreach (OutfitItem v in source)
            {
                ItemView it = v.Item;
                if (v.Color != ColorKey(color) || it.ItemType != 1 || it.Count < 1
                    || (it.InvType != 0 && it.InvType != 1 && it.InvType != 2 && it.InvType != 3)) continue;
                if (!Addr.Valid(it.Ptr) || string.IsNullOrEmpty(v.Model) || v.Mask == 0)
                { plan.Error = "部件信息未完整识别，请在游戏内换装。"; return plan; }
                if ((v.Mask & 4096) != 0) continue;
                if (it.State < 0 || it.State > 2) { plan.Error = "部件状态无法识别。"; return plan; }
                candidates.Add(v);
            }
            candidates.Sort(delegate(OutfitItem a, OutfitItem b) {
                int d = Bits(b.Mask).CompareTo(Bits(a.Mask));
                if (d != 0) return d;
                d = (b.Item.State == 2 ? 1 : 0).CompareTo(a.Item.State == 2 ? 1 : 0);
                if (d != 0) return d;
                d = a.Item.DbId.CompareTo(b.Item.DbId);
                return d != 0 ? d : a.Item.Index.CompareTo(b.Item.Index);
            });
            int occupied = 0;
            HashSet<ulong> pointers = new HashSet<ulong>();
            foreach (OutfitItem v in candidates)
            {
                if ((occupied & v.Mask) != 0 || !pointers.Add(v.Item.Ptr)) { plan.Skipped++; continue; }
                occupied |= v.Mask; plan.Items.Add(v);
            }
            if (plan.Items.Count == 0) plan.Error = "未拥有此配色的可穿戴部件。";
            if (plan.Items.Count > 16) { plan.Items.Clear(); plan.Error = "部件数量超过受支持上限。"; }
            return plan;
        }
        public static void SelfTest(Action<string, bool> check)
        {
            check("换装：解析组合槽位", EquipMask("Bra|UpperDress") == 80);
            check("换装：未知槽位拒绝", EquipMask("Bra|Unknown") == 0);
            check("换装：配色规范化", ColorKey("C1") == "C01" && ModelColor("SailorMizu_Color02_Shirt") == "C02");
            List<OutfitItem> items = new List<OutfitItem>();
            Func<int,int,int,string,OutfitItem> fixture = delegate(int id,int inv,int mask,string color) {
                return new OutfitItem { Item = new ItemView { Ptr = (ulong)(0x20000 + id * 256), DbId = id,
                    ItemType = 1, Count = 1, InvType = inv, Name = "Test" }, Mask = mask, Color = color, Model = "Test_Model" };
            };
            items.Add(fixture(1,3,16,"C01"));items.Add(fixture(2,3,64,"C01"));
            items.Add(fixture(3,3,4,"C02"));items.Add(fixture(4,4,2048,"C01"));items.Add(fixture(5,0,4096,"C01"));
            OutfitPlan p = Build(items,"C01");
            check("换装：仓库已有部件可选择", p.Error == null && p.Items.Count == 2);
            check("换装：其他配色排除", !p.Items.Exists(delegate(OutfitItem v){return v.Item.DbId == 3;}));
            check("换装：商店未购买物品排除", !p.Items.Exists(delegate(OutfitItem v){return v.Item.DbId == 4;}));
            check("换装：发型不替换", !p.Items.Exists(delegate(OutfitItem v){return v.Item.DbId == 5;}));
            items.Add(fixture(6,3,80,"C01"));p=Build(items,"C01");
            check("换装：组合服装避免槽位重叠", p.Items.Count == 1 && p.Items[0].Mask == 80 && p.Skipped == 2);
            check("换装：未拥有配色拒绝", Build(items,"C03").Error != null);
            OutfitItem invalid=fixture(7,3,0,"C01");items.Add(invalid);
            check("换装：缺失部件资料拒绝整次替换", Build(items,"C01").Error != null);
            check("换装：请求结构为 744 字节", Marshal.SizeOf(typeof(OutfitNative.Request)) == 744);
        }
    }

    internal static class OutfitNative
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        internal struct Request
        {
            public uint Magic, Version, Count, Result;
            public ulong Holder, Save;
            public int Preset, Applied;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst=16)] public int[] Ids;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst=16)] public ulong[] Items;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst=512)] public string Message;
        }
        [DllImport("kernel32.dll", SetLastError=true)] private static extern IntPtr OpenProcess(uint access,bool inherit,int pid);
        [DllImport("kernel32.dll", SetLastError=true)] private static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll", SetLastError=true)] private static extern IntPtr VirtualAllocEx(IntPtr h,IntPtr address,UIntPtr size,uint allocation,uint protect);
        [DllImport("kernel32.dll", SetLastError=true)] private static extern bool VirtualFreeEx(IntPtr h,IntPtr address,UIntPtr size,uint freeType);
        [DllImport("kernel32.dll", SetLastError=true)] private static extern bool WriteProcessMemory(IntPtr h,IntPtr address,byte[] data,UIntPtr size,out UIntPtr done);
        [DllImport("kernel32.dll", SetLastError=true)] private static extern bool ReadProcessMemory(IntPtr h,IntPtr address,byte[] data,UIntPtr size,out UIntPtr done);
        [DllImport("kernel32.dll", SetLastError=true)] private static extern IntPtr CreateRemoteThread(IntPtr h,IntPtr attributes,UIntPtr stack,IntPtr entry,IntPtr parameter,uint flags,out uint id);
        [DllImport("kernel32.dll", SetLastError=true)] private static extern uint WaitForSingleObject(IntPtr h,uint milliseconds);
        [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
        [DllImport("kernel32.dll", CharSet=CharSet.Ansi)] private static extern IntPtr GetProcAddress(IntPtr module,string name);
        [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] private static extern bool GetModuleHandleEx(uint flags,IntPtr address,out IntPtr module);
        [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] private static extern uint GetModuleFileName(IntPtr module,StringBuilder name,int size);
        [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] private static extern IntPtr LoadLibraryEx(string file,IntPtr reserved,uint flags);
        [DllImport("kernel32.dll")] private static extern bool FreeLibrary(IntPtr module);
        private static string Extract()
        {
            byte[] bytes;
            using (Stream input=Assembly.GetExecutingAssembly().GetManifestResourceStream("LilliaOutfitBridge.dll"))
            {
                if (input==null) throw new InvalidOperationException("换装组件未嵌入程序。");
                using (MemoryStream copy=new MemoryStream()) { input.CopyTo(copy);bytes=copy.ToArray(); }
            }
            string key;
            using (SHA256 h=SHA256.Create()) key=BitConverter.ToString(h.ComputeHash(bytes)).Replace("-","");
            string dir=Path.Combine(Path.GetTempPath(),"LilliaTrainer",key);
            Directory.CreateDirectory(dir);
            string path=Path.Combine(dir,"LilliaOutfitBridge.dll");
            if (!File.Exists(path)) File.WriteAllBytes(path,bytes);
            using (SHA256 h=SHA256.Create())
            using (Stream input=File.OpenRead(path))
                if (BitConverter.ToString(h.ComputeHash(input)).Replace("-","")!=key)
                    throw new InvalidOperationException("换装组件校验失败。");
            return path;
        }
        private static IntPtr Module(Process target,string name)
        {
            foreach (ProcessModule m in target.Modules)
                if (string.Equals(m.ModuleName,name,StringComparison.OrdinalIgnoreCase)) return m.BaseAddress;
            throw new InvalidOperationException("目标进程缺少模块："+name);
        }
        private static IntPtr BridgeModule(Process target,string path)
        {
            foreach(ProcessModule m in target.Modules)
                if(string.Equals(m.FileName,path,StringComparison.OrdinalIgnoreCase))return m.BaseAddress;
            return IntPtr.Zero;
        }
        private static IntPtr Loader(Process target)
        {
            IntPtr address=GetProcAddress(GetModuleHandle("kernel32.dll"),"LoadLibraryW"),owner;
            if (address==IntPtr.Zero || !GetModuleHandleEx(6,address,out owner)) throw new InvalidOperationException("无法定位系统加载器。");
            StringBuilder name=new StringBuilder(1024);GetModuleFileName(owner,name,name.Capacity);
            return new IntPtr(Module(target,Path.GetFileName(name.ToString())).ToInt64()+address.ToInt64()-owner.ToInt64());
        }
        private static IntPtr Allocate(IntPtr h,byte[] bytes)
        {
            IntPtr p=VirtualAllocEx(h,IntPtr.Zero,new UIntPtr((uint)bytes.Length),0x3000,4);
            if (p==IntPtr.Zero) throw new InvalidOperationException("无法建立换装请求缓冲。");
            UIntPtr done;
            if (!WriteProcessMemory(h,p,bytes,new UIntPtr((uint)bytes.Length),out done) || done.ToUInt64()!=(ulong)bytes.Length)
            { VirtualFreeEx(h,p,UIntPtr.Zero,0x8000);throw new InvalidOperationException("无法传递换装请求。"); }
            return p;
        }
        private static bool ThreadCall(IntPtr h,IntPtr entry,IntPtr p,uint timeout)
        {
            uint id;IntPtr t=CreateRemoteThread(h,IntPtr.Zero,UIntPtr.Zero,entry,p,0,out id);
            if (t==IntPtr.Zero) throw new InvalidOperationException("游戏未允许加载换装组件。");
            try { return WaitForSingleObject(t,timeout)==0; }
            finally { CloseHandle(t); }
        }
        public static Request Apply(Gen expected,int preset,OutfitPlan plan)
        {
            Request request=new Request { Magic=0x4c4c4f46,Version=1,Count=(uint)plan.Items.Count,
                Holder=expected.Holder,Save=expected.Save,Preset=preset,Result=99,
                Ids=new int[16],Items=new ulong[16],Message="" };
            for (int i=0;i<plan.Items.Count;i++) { request.Ids[i]=plan.Items[i].Item.DbId;request.Items[i]=plan.Items[i].Item.Ptr; }
            string bridge=Extract();
            using (Process target=Process.GetProcessById(expected.Pid))
            {
                if (target.StartTime.Ticks!=expected.Start) throw new InvalidOperationException("游戏进程已变化，换装已取消。");
                string gameDir=Path.GetDirectoryName(target.MainModule.FileName);
                using (SHA256 hash=SHA256.Create())
                using (Stream dll=File.OpenRead(Path.Combine(gameDir,Cfg.AssemblyName)))
                    if (BitConverter.ToString(hash.ComputeHash(dll)).Replace("-","")!=Cfg.AssemblySha256)
                        throw new InvalidOperationException("当前游戏版本不支持换装。");
                IntPtr h=OpenProcess(0x043A,false,expected.Pid);
                if (h==IntPtr.Zero) throw new InvalidOperationException("无法获得换装组件所需权限。");
                IntPtr pathBuffer=IntPtr.Zero,requestBuffer=IntPtr.Zero;
                bool loaderDone=false,requestDone=false;
                try {
                    IntPtr remoteModule=BridgeModule(target,bridge);
                    if(remoteModule==IntPtr.Zero) {
                        pathBuffer=Allocate(h,Encoding.Unicode.GetBytes(bridge+"\0"));
                        loaderDone=ThreadCall(h,Loader(target),pathBuffer,10000);
                        if (!loaderDone) throw new InvalidOperationException("组件加载超时；请检查游戏后重试。");
                        target.Refresh();remoteModule=BridgeModule(target,bridge);
                        if(remoteModule==IntPtr.Zero)throw new InvalidOperationException("换装组件未成功载入。");
                    }
                    IntPtr local=LoadLibraryEx(bridge,IntPtr.Zero,1);
                    if (local==IntPtr.Zero) throw new InvalidOperationException("无法读取换装组件。");
                    long offset;
                    try { IntPtr entry=GetProcAddress(local,"LilliaOutfitRun");if(entry==IntPtr.Zero)throw new InvalidOperationException("换装接口不存在。");offset=entry.ToInt64()-local.ToInt64(); }
                    finally { FreeLibrary(local); }
                    int size=Marshal.SizeOf(typeof(Request));byte[] buffer=new byte[size];IntPtr block=Marshal.AllocHGlobal(size);
                    try { Marshal.StructureToPtr(request,block,false);Marshal.Copy(block,buffer,0,size); }
                    finally { Marshal.FreeHGlobal(block); }
                    requestBuffer=Allocate(h,buffer);
                    requestDone=ThreadCall(h,new IntPtr(remoteModule.ToInt64()+offset),requestBuffer,45000);
                    if (!requestDone) throw new InvalidOperationException("换装任务尚未结束；请先在游戏中确认，避免重复操作。");
                    UIntPtr done;
                    if (!ReadProcessMemory(h,requestBuffer,buffer,new UIntPtr((uint)size),out done) || done.ToUInt64()!=(ulong)size)
                        throw new InvalidOperationException("无法读取换装结果，请在游戏中确认。");
                    block=Marshal.AllocHGlobal(size);
                    try { Marshal.Copy(buffer,0,block,size);request=(Request)Marshal.PtrToStructure(block,typeof(Request)); }
                    finally { Marshal.FreeHGlobal(block); }
                    return request;
                } finally {
                    // Outstanding worker arguments must remain valid until that worker finishes.
                    if(loaderDone && pathBuffer!=IntPtr.Zero)VirtualFreeEx(h,pathBuffer,UIntPtr.Zero,0x8000);
                    if(requestDone && requestBuffer!=IntPtr.Zero)VirtualFreeEx(h,requestBuffer,UIntPtr.Zero,0x8000);
                    CloseHandle(h);
                }
            }
        }
        public static string Explain(string s)
        {
            if (s == null) return "换装失败，请在游戏内确认。";
            if (s.Contains("outfit window")) return "请先打开基地换装界面，再点击替换。";
            if (s.Contains("storage dimensions")) return "仓库尺寸无法识别，本次没有修改。";
            if (s.Contains("storage")) return "仓库没有足够的连续空位，本次没有修改。";
            if (s.Contains("busy")) return "角色正在加载或换装，请稍后再试。";
            if (s.Contains("save or preset")) return "存档或预设已切换，换装已取消。";
            if (s.Contains("context")) return "游戏主线程换装接口暂不可用，请重新打开换装界面。";
            if (s.Contains("cancelled")) return "游戏未及时处理换装命令，已取消。";
            return "换装未完成，请在游戏内确认。诊断：" + s;
        }
    }

    internal static class OutfitProbe
    {
        public static int Run()
        {
            string dir=AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            foreach(Process p in Process.GetProcessesByName(Cfg.ProcessName))
            using(p)
            {
                if(!string.Equals(Path.GetDirectoryName(p.MainModule.FileName),dir,StringComparison.OrdinalIgnoreCase))continue;
                string hash;
                using(SHA256 sha=SHA256.Create())using(Stream input=File.OpenRead(Path.Combine(dir,Cfg.AssemblyName)))
                    hash=BitConverter.ToString(sha.ComputeHash(input)).Replace("-","");
                if(hash!=Cfg.AssemblySha256){Console.WriteLine("{\"error\":\"version mismatch\"}");return 2;}
                ulong game=0;
                foreach(ProcessModule mod in p.Modules)if(string.Equals(mod.ModuleName,Cfg.AssemblyName,StringComparison.OrdinalIgnoreCase))game=(ulong)mod.BaseAddress.ToInt64();
                using(ProcessMemory memory=new ProcessMemory(p.Id))
                {
                    Chain c;string error;List<ItemView> items;
                    if(game==0 || !Resolver.Resolve(memory,game,out c,out error) || c.Save==0
                        || !Data.ReadInventory(memory,c,out items,out error))
                    {Console.WriteLine("{\"error\":\"inventory unavailable\"}");return 3;}
                    ulong dm=M.PtrOr0(memory,c.Holder+0x68);
                    if(dm==0)dm=M.PtrOr0(memory,c.Holder+0x130);
                    int mode=-1,preset=-1;
                    if(M.ClassName(memory,dm)=="DressManager")M.ReadI32(memory,dm+0x28,out mode);
                    M.ReadI32(memory,c.Save+(ulong)Off.SelectedPresetIndex,out preset);
                    StringBuilder result=new StringBuilder("{\"mode\":\"outfit-probe\",\"hashOk\":true,\"outfitWindow\":");
                    result.Append(mode==4?"true":"false").Append(",\"preset\":").Append(preset).Append(",\"warehouses\":[");
                    for(int kind=0;kind<3;kind++)
                    {
                        ulong address;int columns,rows;string capacityError;
                        bool readable=WarehouseCapacity.TryRead(memory,c,kind,out address,out columns,out rows,out capacityError);
                        if(kind>0)result.Append(',');
                        result.Append("{\"container\":").Append(kind==0?3:(kind==1?10:11))
                            .Append(",\"readable\":").Append(readable?"true":"false");
                        if(readable)
                        {
                            result.Append(",\"columns\":").Append(columns).Append(",\"rows\":").Append(rows)
                                .Append(",\"cells\":").Append(columns*rows);
                            long requiredColumns,requiredRows;string layoutError;
                            bool layoutReadable=WarehouseCapacity.TryGetRequiredSize(memory,c,kind,
                                out requiredColumns,out requiredRows,out layoutError);
                            result.Append(",\"layoutReadable\":").Append(layoutReadable?"true":"false");
                            if(layoutReadable)result.Append(",\"requiredColumns\":").Append(requiredColumns)
                                .Append(",\"requiredRows\":").Append(requiredRows);
                        }
                        result.Append('}');
                    }
                    result.Append("],\"plans\":[");
                    bool first=true;
                    foreach(DressCatalog.Series s in DressCatalog.All)
                    {
                        if(s.Key!="DefaultDress" && s.Key!="Lottie" && s.Key!="SailorMizu" && s.Key!="Bunny_Police" && s.Key!="Cold_Blood")continue;
                        List<OutfitItem> data=OutfitPlanner.Read(memory,items,s);
                        foreach(string color in s.Colors)
                        {
                            OutfitPlan plan=OutfitPlanner.Build(data,color);
                            if(!first)result.Append(',');first=false;
                            result.Append("{\"series\":\"").Append(s.Key).Append("\",\"color\":\"").Append(OutfitPlanner.ColorKey(color))
                                .Append("\",\"ownedPieces\":").Append(plan.Items.Count).Append(",\"ready\":").Append(plan.Error==null?"true":"false").Append('}');
                        }
                    }
                    result.Append("],\"writeHandleOpened\":").Append(memory.CanWrite?"true":"false").Append('}');
                    Console.WriteLine(result.ToString());return memory.CanWrite?4:0;
                }
            }
            Console.WriteLine("{\"error\":\"game not running\"}");return 1;
        }
    }

    /// <summary>换装诊断日志：仅在用户真实点击换装时写入程序目录下的 trainer\outfit.log，不记录存档内容。</summary>
    internal static class OutfitDiagnostics
    {
        private static string Sanitize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\r"," ").Replace("\n"," ").Replace("\t"," ");
        }
        internal static string FormatLogLine(DateTimeOffset when,int pid,string stage,int preset,string series,string color,
            int count,int? result,int? applied,string message,string detail)
        {
            StringBuilder sb=new StringBuilder();
            sb.Append(when.ToString("yyyy-MM-dd HH:mm:ss zzz",CultureInfo.InvariantCulture));
            sb.Append(" pid=").Append(pid.ToString(CultureInfo.InvariantCulture));
            sb.Append(" stage=").Append(Sanitize(stage));
            sb.Append(" preset=").Append(preset.ToString(CultureInfo.InvariantCulture));
            sb.Append(" series=").Append(Sanitize(series));
            sb.Append(" color=").Append(Sanitize(color));
            sb.Append(" count=").Append(count.ToString(CultureInfo.InvariantCulture));
            sb.Append(" result=").Append(result.HasValue ? result.Value.ToString(CultureInfo.InvariantCulture) : "n/a");
            sb.Append(" applied=").Append(applied.HasValue ? applied.Value.ToString(CultureInfo.InvariantCulture) : "n/a");
            sb.Append(" message=").Append(Sanitize(message));
            if (!string.IsNullOrEmpty(detail)) sb.Append(" detail=").Append(Sanitize(detail));
            return sb.ToString();
        }
        /// <summary>写入失败绝不抛出：换装原始结果必须原样保留，不能因日志问题误报。</summary>
        internal static void Log(int pid,string stage,int preset,string series,string color,int count,
            int? result,int? applied,string message,string detail)
        {
            try
            {
                string line=FormatLogLine(DateTimeOffset.Now,pid,stage,preset,series,color,count,result,applied,message,detail);
                string dir=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"trainer");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir,"outfit.log"),line+Environment.NewLine,new UTF8Encoding(false));
            }
            catch { }
        }
    }

    internal sealed partial class MainForm
    {
        private ComboBox cboOutfitColor;
        private Button btnOutfit;
        private Label lblOutfit;
        private bool outfitBusy, outfitSelecting;
        private List<OutfitItem> outfitCandidates=new List<OutfitItem>();
        private Gen outfitSelectionGen;
        private int outfitSelectionPreset;
        // 最近一次换装的稳定结果；非 null 时轮询不得覆盖，直到用户显式更换系列/配色、重新操作或进程/存档切换。
        private string outfitPinnedResult;
        private bool outfitPinnedOk;
        private bool outfitSuppressSelect;

        private void SetOutfitPinned(string text,bool ok)
        {
            outfitPinnedResult=text;outfitPinnedOk=ok;
            if(lblOutfit!=null)lblOutfit.Text=text;
        }
        private void ClearOutfitPinned() { outfitPinnedResult=null;outfitPinnedOk=false; }
        private void OnOutfitColorChanged()
        {
            if(outfitSuppressSelect)return;
            ClearOutfitPinned();
            UpdateOutfitPlan();
        }
        private Control OutfitControls()
        {
            cboOutfitColor=Cbo(100,new string[]{"C01"},0);
            btnOutfit=Btn("一键替换套装",130,delegate(object s,EventArgs e){ApplyOutfit();});
            cboOutfitColor.SelectedIndexChanged+=delegate(object s,EventArgs e){OnOutfitColorChanged();};
            lvDress.SelectedIndexChanged+=delegate(object s,EventArgs e){SelectOutfitSeries();};
            lblOutfit=CardLabel("选择一个已拥有的系列，再选择配色。",630);
            btnOutfit.Enabled=false;
            return Row(32,Lbl("套装配色",74,false),cboOutfitColor,btnOutfit,lblOutfit);
        }
        private void SelectOutfitSeries()
        {
            if (outfitSelecting || outfitBusy) return;
            ClearOutfitPinned();
            outfitCandidates.Clear();outfitSelectionGen=null;
            cboOutfitColor.Items.Clear();btnOutfit.Enabled=false;
            if (lvDress.SelectedIndices.Count==0) { lblOutfit.Text="选择一个已拥有的系列，再选择配色。";return; }
            int index=lvDress.SelectedIndices[0];
            if(index<0 || index>=DressCatalog.All.Length)return;
            DressCatalog.Series series=DressCatalog.All[index];
            if (PreviewOnly) {
                outfitSuppressSelect=true;
                try {
                    foreach(string color in series.Colors)cboOutfitColor.Items.Add(OutfitPlanner.ColorKey(color));
                    if(cboOutfitColor.Items.Count>0)cboOutfitColor.SelectedIndex=0;
                } finally { outfitSuppressSelect=false; }
                lblOutfit.Text="预览：替换已有部件，保留发型；在游戏基地换装界面执行。";return;
            }
            Gen expected=gen;Chain chain;string error;List<ItemView> items;
            if (!FreshChain(out chain,out error) || !WritePolicy.Current(expected,pid,ProcStart(),chain)
                || !Data.ReadInventory(mem,chain,out items,out error))
            { lblOutfit.Text="请先载入存档并刷新修改器。";return; }
            int preset;
            if(!M.ReadI32(mem,chain.Save+(ulong)Off.SelectedPresetIndex,out preset) || preset<0 || preset>3)
            { lblOutfit.Text="当前预设无法识别。";return; }
            outfitCandidates=OutfitPlanner.Read(mem,items,series);outfitSelectionGen=expected;outfitSelectionPreset=preset;
            outfitSuppressSelect=true;
            try {
                foreach(string color in series.Colors)cboOutfitColor.Items.Add(OutfitPlanner.ColorKey(color));
                foreach(OutfitItem it in outfitCandidates)if(!cboOutfitColor.Items.Contains(it.Color))cboOutfitColor.Items.Add(it.Color);
                for(int i=0;i<cboOutfitColor.Items.Count;i++)
                    if(OutfitPlanner.Build(outfitCandidates,(string)cboOutfitColor.Items[i]).Error==null){cboOutfitColor.SelectedIndex=i;break;}
                if(cboOutfitColor.SelectedIndex<0 && cboOutfitColor.Items.Count>0)cboOutfitColor.SelectedIndex=0;
            } finally { outfitSuppressSelect=false; }
            UpdateOutfitPlan();
        }
        private void UpdateOutfitPlan()
        {
            if(lblOutfit==null || outfitBusy)return;
            OutfitPlan plan=OutfitPlanner.Build(outfitCandidates,cboOutfitColor.Text);
            btnOutfit.Enabled=!PreviewOnly && outfitSelectionGen!=null && plan.Error==null;
            if(outfitPinnedResult!=null){lblOutfit.Text=outfitPinnedResult;return;}
            string title=lvDress.SelectedIndices.Count>0 ? DressCatalog.All[lvDress.SelectedIndices[0]].Zh+" · " : "";
            lblOutfit.Text=plan.Error ?? (title+"替换已有 " + plan.Items.Count + " 件；保留发型及未覆盖部位。"
                +(plan.Skipped>0 ? " 已避开重复槽位。" : ""));
        }
        private void UpdateOutfitEnabled(bool connected)
        {
            if(btnOutfit==null)return;
            if(outfitBusy){btnOutfit.Enabled=false;cboOutfitColor.Enabled=false;return;}
            if(!connected){btnOutfit.Enabled=false;outfitSelectionGen=null;ClearOutfitPinned();return;}
            if(outfitSelectionGen!=null && (gen==null || !outfitSelectionGen.Same(gen.Pid,gen.Start,gen.Holder,gen.Save)))
            { outfitSelectionGen=null;ClearOutfitPinned(); }
            UpdateOutfitPlan();
        }
        private void ApplyOutfit()
        {
            if(PreviewOnly || outfitBusy || outfitSelectionGen==null)return;
            Gen expected=outfitSelectionGen;Chain chain;string error;
            int selIx=lvDress.SelectedIndices.Count>0 ? lvDress.SelectedIndices[0] : -1;
            string seriesName=(selIx>=0 && selIx<DressCatalog.All.Length) ? DressCatalog.All[selIx].Zh : "";
            string colorKey=cboOutfitColor.Text;
            if(!FreshChain(out chain,out error) || !WritePolicy.Current(expected,pid,ProcStart(),chain))
            {
                string msg="换装未执行：存档或进程已变化，请重新选择系列。";
                SetOutfitPinned(msg,false);
                OutfitDiagnostics.Log(expected.Pid,"precheck",outfitSelectionPreset,seriesName,colorKey,0,null,null,msg,error);
                outfitSelectionGen=null;btnOutfit.Enabled=false;return;
            }
            int preset;
            if(!M.ReadI32(mem,chain.Save+(ulong)Off.SelectedPresetIndex,out preset) || preset!=outfitSelectionPreset)
            {
                string msg="换装未执行：游戏预设已切换，请重新选择系列。";
                SetOutfitPinned(msg,false);
                OutfitDiagnostics.Log(expected.Pid,"precheck",outfitSelectionPreset,seriesName,colorKey,0,null,null,msg,"preset changed");
                return;
            }
            OutfitPlan plan=OutfitPlanner.Build(outfitCandidates,cboOutfitColor.Text);
            if(plan.Error!=null)
            {
                string msg="换装未执行："+plan.Error;
                SetOutfitPinned(msg,false);
                OutfitDiagnostics.Log(expected.Pid,"precheck",preset,seriesName,colorKey,0,null,null,msg,"plan error");
                return;
            }
            outfitBusy=true;btnOutfit.Enabled=false;cboOutfitColor.Enabled=false;lvDress.Enabled=false;
            lblOutfit.Text="正在调用游戏原生换装流程……";
            int count=plan.Items.Count;
            ThreadPool.QueueUserWorkItem(delegate(object ignored) {
                string message;bool ok=false;
                try {
                    OutfitNative.Request result=OutfitNative.Apply(expected,preset,plan);ok=result.Result==0 && result.Applied>0;
                    message=ok ? "已替换 "+result.Applied+" 件；外观正在加载，请在游戏内检查并保存。" : "换装未执行："+OutfitNative.Explain(result.Message);
                    OutfitDiagnostics.Log(expected.Pid,"native",preset,seriesName,colorKey,count,(int)result.Result,(int)result.Applied,result.Message,"");
                } catch(Exception ex) { message="换装未执行："+ex.Message;ok=false;OutfitDiagnostics.Log(expected.Pid,"exception",preset,seriesName,colorKey,count,null,null,ex.Message,ex.GetType().Name); }
                if(IsDisposed || closing)return;
                try { BeginInvoke((MethodInvoker)delegate {
                    outfitBusy=false;cboOutfitColor.Enabled=true;lvDress.Enabled=true;
                    Tick(null,null);RefreshWardrobe(snap.Items);SetOutfitPinned(message,ok);SetStatus(message,ok);
                }); } catch(InvalidOperationException) { }
            });
        }
        internal void OutfitSafetySelfTest(Action<string,bool> check)
        {
            check("换装：预览不能执行写入",PreviewOnly && !btnOutfit.Enabled);
            ApplyOutfit();
            check("换装：无选择时不连接游戏",mem==null && !outfitBusy);
            SetOutfitPinned("换装未执行：离线回归失败提示",false);
            UpdateOutfitPlan();UpdateOutfitPlan();
            check("换装：轮询不抹去最近失败结果",outfitPinnedResult=="换装未执行：离线回归失败提示" && lblOutfit.Text=="换装未执行：离线回归失败提示");
            OnOutfitColorChanged();
            check("换装：显式更改配色后清空结果",outfitPinnedResult==null && lblOutfit.Text!=null
                && lblOutfit.Text.IndexOf("离线回归失败提示",StringComparison.Ordinal)<0);
            string line=OutfitDiagnostics.FormatLogLine(new DateTimeOffset(2026,10,7,1,2,3,TimeSpan.Zero),4242,"native",2,"SailorMizu","C02",3,7,1,"outfit window not open","");
            check("换装：日志保留原生失败码与消息",line.IndexOf("result=7",StringComparison.Ordinal)>=0
                && line.IndexOf("applied=1",StringComparison.Ordinal)>=0 && line.IndexOf("preset=2",StringComparison.Ordinal)>=0
                && line.IndexOf("outfit window not open",StringComparison.Ordinal)>=0);
        }
        internal void PrepareOutfitPreview()
        {
            if(PreviewOnly && lvDress.Items.Count>2)lvDress.Items[2].Selected=true;
        }
    }
}
