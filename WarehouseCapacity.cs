using System;
using System.Collections.Generic;
using System.Text;

namespace LilliaTrainer
{
    internal static class WarehouseCapacity
    {
        public const int MaxAxis = 256;
        public const int MaxCells = 4096;

        public static bool TryField(int kind, out int offset, out int container)
        {
            offset = 0; container = 0;
            if (kind == 0) { offset = 0x28; container = 3; return true; }
            if (kind == 1) { offset = 0x40; container = 10; return true; }
            if (kind == 2) { offset = 0x48; container = 11; return true; }
            return false;
        }

        public static bool TryRead(IMemory memory, Chain chain, int kind,
            out ulong address, out int columns, out int rows, out string error)
        {
            address = 0; columns = 0; rows = 0; error = null;
            int offset, container;
            if (!TryField(kind, out offset, out container))
            { error = "只能选择仓库、消耗品仓库或杂物仓库"; return false; }
            if (memory == null || chain == null || !Addr.Valid(chain.Save))
            { error = "存档未载入"; return false; }
            ulong inventory = M.PtrOr0(memory, chain.Save + (ulong)Off.Inventory);
            if (!Addr.Valid(inventory) || M.ClassName(memory, inventory) != "InventoryData")
            { error = "仓库对象类型不匹配"; return false; }
            address = inventory + (ulong)offset;
            float x, y;
            if (!M.ReadF32(memory, address, out x) || !M.ReadF32(memory, address + 4, out y)
                || !Limits.F32(x, 1, MaxAxis) || !Limits.F32(y, 1, MaxAxis)
                || x != (float)Math.Truncate(x) || y != (float)Math.Truncate(y)
                || (double)x * y > MaxCells)
            { address = 0; error = "当前仓库尺寸无效或超出支持范围"; return false; }
            columns = (int)x; rows = (int)y;
            return true;
        }

        public static string Expand(IMemory memory, Chain chain, int kind,
            int columns, int rows, Func<bool> stillCurrent)
        {
            ulong address;
            int oldColumns, oldRows;
            string error;
            if (!TryRead(memory, chain, kind, out address, out oldColumns, out oldRows, out error)) return error;
            if (columns < 1 || columns > MaxAxis || rows < 1 || rows > MaxAxis
                || (long)columns * rows > MaxCells)
                return "单边尺寸需为 1..256，且总容量不能超过 4096 格";
            if (columns < oldColumns || rows < oldRows) return "扩容不能缩小任一边，避免覆盖现有物品";
            if (columns == oldColumns && rows == oldRows) return "目标尺寸与当前仓库相同";
            long requiredColumns, requiredRows;
            if (!TryGetRequiredSize(memory, chain, kind, out requiredColumns, out requiredRows, out error)) return error;
            // Expansion can recover items outside an older grid, without moving them.
            // All existing rectangles must fit the requested grid before any write.
            if (requiredColumns > columns || requiredRows > rows)
                return "目标容量不足，现有物品至少需要 " + Math.Max(oldColumns, requiredColumns)
                    + " 列 × " + Math.Max(oldRows, requiredRows) + " 行";
            byte[] before = new byte[8];
            if (!memory.ReadBytes(address, before, 0, 8)
                || BitConverter.ToSingle(before, 0) != oldColumns
                || BitConverter.ToSingle(before, 4) != oldRows)
                return "仓库尺寸已变化，请刷新后重试";
            // Recheck the displayed save immediately before the only mutation.
            if (stillCurrent == null || !stillCurrent()) return "存档或进程身份已变化，本次扩容已拒绝";
            ulong latestAddress;
            int latestColumns, latestRows;
            if (!TryRead(memory, chain, kind, out latestAddress, out latestColumns, out latestRows, out error)
                || latestAddress != address || latestColumns != oldColumns || latestRows != oldRows)
                return "仓库对象或尺寸已变化，请刷新后重试";
            byte[] after = new byte[8];
            Buffer.BlockCopy(BitConverter.GetBytes((float)columns), 0, after, 0, 4);
            Buffer.BlockCopy(BitConverter.GetBytes((float)rows), 0, after, 4, 4);
            if (memory.WriteBytes(address, after, 0, 8) && Matches(memory, address, after)) return null;
            bool restored = memory.WriteBytes(address, before, 0, 8) && Matches(memory, address, before);
            return restored ? "扩容写入校验失败，已恢复原尺寸"
                : "扩容写入及回退校验失败；请勿保存，重新载入存档恢复";
        }

        public static bool TryGetRequiredSize(IMemory memory, Chain chain, int kind,
            out long columns, out long rows, out string error)
        {
            columns = 0; rows = 0; error = null;
            int offset, container;
            if (!TryField(kind, out offset, out container))
            { error = "只能选择仓库、消耗品仓库或杂物仓库"; return false; }
            if (memory == null || chain == null || !Addr.Valid(chain.Save))
            { error = "存档未载入"; return false; }
            List<ItemView> items;
            if (!Data.ReadInventory(memory, chain, out items, out error)) return false;
            foreach (ItemView item in items)
            {
                if (item.InvType != container) continue;
                int x, y, width, height;
                if (!M.ReadI32(memory, item.Ptr + (ulong)Off.ItemX, out x)
                    || !M.ReadI32(memory, item.Ptr + (ulong)Off.ItemY, out y)
                    || !M.ReadI32(memory, item.Ptr + 0x28, out width)
                    || !M.ReadI32(memory, item.Ptr + 0x2C, out height))
                { error = "无法读取仓库物品 ID " + item.DbId + " 的坐标或尺寸，请刷新容量后重试"; return false; }
                if (x < 0 || y < 0 || width < 1 || height < 1)
                { error = "仓库物品 ID " + item.DbId + " 的坐标或尺寸无效，请先在游戏内整理"; return false; }
                columns = Math.Max(columns, (long)x + width);
                rows = Math.Max(rows, (long)y + height);
            }
            return true;
        }

        private static bool Matches(IMemory memory, ulong address, byte[] expected)
        {
            byte[] actual = new byte[expected.Length];
            if (!memory.ReadBytes(address, actual, 0, actual.Length)) return false;
            for (int i = 0; i < actual.Length; i++) if (actual[i] != expected[i]) return false;
            return true;
        }

        // All fixtures use the trainer's own byte array; no game process is opened.
        public static void SelfTest(Action<string, bool> check)
        {
            MockMemory memory = new MockMemory(0x10000);
            ulong save = MockMemory.Base + 0x1000, inventory = MockMemory.Base + 0x2000;
            ulong klass = MockMemory.Base + 0x3000, name = MockMemory.Base + 0x4000;
            ulong list = MockMemory.Base + 0x5000, array = MockMemory.Base + 0x6000;
            ulong item = MockMemory.Base + 0x7000;
            PutPointer(memory, save + (ulong)Off.Inventory, inventory);
            PutPointer(memory, inventory, klass); PutPointer(memory, klass + (ulong)Off.KlassName, name);
            byte[] ascii = Encoding.ASCII.GetBytes("InventoryData\0");
            memory.WriteBytes(name, ascii, 0, ascii.Length);
            PutPointer(memory, inventory + (ulong)Off.InvList, list);
            M.WriteI32(memory, list + (ulong)Off.ListSize, 0);
            foreach (int at in new int[] { 0x28, 0x40, 0x48 })
            { M.WriteF32(memory, inventory + (ulong)at, 10); M.WriteF32(memory, inventory + (ulong)at + 4, 80); }
            Chain chain = new Chain { Save = save };
            ulong address; int columns, rows; string error;
            Func<bool> current = delegate { return true; };
            check("仓库尺寸以两个 float 正确读取", TryRead(memory, chain, 0, out address, out columns, out rows, out error)
                && columns == 10 && rows == 80);
            check("非仓库容器不能扩容", !TryRead(memory, chain, 3, out address, out columns, out rows, out error));
            check("仓库不允许缩小", Expand(memory, chain, 0, 9, 160, current) != null);
            check("仓库不允许非正尺寸", Expand(memory, chain, 0, 0, 160, current) != null);
            check("仓库单边尺寸上限", Expand(memory, chain, 0, 10, 257, current) != null);
            check("仓库总格数上限", Expand(memory, chain, 0, 256, 256, current) != null);
            check("仓库相同尺寸拒绝写入", Expand(memory, chain, 0, 10, 80, current) != null);
            check("存档变化拒绝扩容", Expand(memory, chain, 0, 10, 160, delegate { return false; }) != null
                && TryRead(memory, chain, 0, out address, out columns, out rows, out error) && rows == 80);
            check("仓库扩容 800 到 1600 格", Expand(memory, chain, 0, 10, 160, current) == null
                && TryRead(memory, chain, 0, out address, out columns, out rows, out error) && columns == 10 && rows == 160);
            check("扩容不影响另一仓库", TryRead(memory, chain, 1, out address, out columns, out rows, out error) && rows == 80
                && TryRead(memory, chain, 2, out address, out columns, out rows, out error) && rows == 80);
            M.WriteF32(memory, inventory + 0x28, 10.5f);
            check("小数尺寸被拒绝", !TryRead(memory, chain, 0, out address, out columns, out rows, out error));
            M.WriteF32(memory, inventory + 0x28, float.NaN);
            check("NaN 尺寸被拒绝", !TryRead(memory, chain, 0, out address, out columns, out rows, out error));
            M.WriteF32(memory, inventory + 0x28, 10); M.WriteF32(memory, inventory + 0x2C, 80);
            PutPointer(memory, list + (ulong)Off.ListItems, array);
            PutPointer(memory, array + (ulong)Off.ArrayData, item);
            PutPointer(memory, array + (ulong)Off.ArrayLength, 1);
            M.WriteI32(memory, list + (ulong)Off.ListSize, 1);
            M.WriteI32(memory, item + (ulong)Off.ItemInvType, 3);
            M.WriteI32(memory, item + 0x28, 1); M.WriteI32(memory, item + 0x2C, 1);
            M.WriteI32(memory, item + (ulong)Off.ItemX, 10); M.WriteI32(memory, item + (ulong)Off.ItemY, 79);
            check("物品超出目标网格时拒绝扩容", Expand(memory, chain, 0, 10, 160, current) != null);
            M.WriteI32(memory, item + (ulong)Off.ItemX, 9);
            check("合法物品布局允许扩容", Expand(memory, chain, 0, 10, 160, current) == null);
            int x, y;
            check("扩容保持物品坐标", M.ReadI32(memory, item + (ulong)Off.ItemX, out x) && x == 9
                && M.ReadI32(memory, item + (ulong)Off.ItemY, out y) && y == 79);
            // Reproduce the live failure: an 80-row grid with items extending to row 133.
            M.WriteF32(memory, inventory + 0x2C, 80);
            M.WriteI32(memory, item + (ulong)Off.ItemY, 132);
            long requiredColumns, requiredRows;
            check("旧网格之外的物品所需尺寸可读", TryGetRequiredSize(memory, chain, 0,
                out requiredColumns, out requiredRows, out error) && requiredColumns == 10 && requiredRows == 133);
            error = Expand(memory, chain, 0, 10, 120, current);
            check("目标不足提示最小尺寸且不写入", error != null && error.Contains("133")
                && TryRead(memory, chain, 0, out address, out columns, out rows, out error) && rows == 80);
            check("物品超旧网格但落在目标内允许扩容", Expand(memory, chain, 0, 10, 160, current) == null
                && TryRead(memory, chain, 0, out address, out columns, out rows, out error) && rows == 160);
            check("恢复仓库范围不改物品坐标", M.ReadI32(memory, item + (ulong)Off.ItemX, out x) && x == 9
                && M.ReadI32(memory, item + (ulong)Off.ItemY, out y) && y == 132);
            M.WriteF32(memory, inventory + 0x2C, 80);
            M.WriteI32(memory, item + (ulong)Off.ItemY, -1);
            check("负坐标仍拒绝扩容", Expand(memory, chain, 0, 10, 160, current) != null
                && TryRead(memory, chain, 0, out address, out columns, out rows, out error) && rows == 80);
            M.WriteI32(memory, item + (ulong)Off.ItemY, 79);
            M.WriteI32(memory, item + 0x2C, 0);
            check("无效物品尺寸仍拒绝扩容", Expand(memory, chain, 0, 10, 160, current) != null
                && TryRead(memory, chain, 0, out address, out columns, out rows, out error) && rows == 80);
            M.WriteI32(memory, item + 0x2C, 1);
            M.WriteI32(memory, item + (ulong)Off.ItemX, int.MaxValue);
            error = Expand(memory, chain, 0, 12, 160, current);
            check("物品范围相加不溢出绕过边界", error != null && error.Contains("2147483648"));
            M.WriteI32(memory, item + (ulong)Off.ItemX, 9);
            M.WriteF32(memory, inventory + 0x2C, 80);
            FaultOnceMemory faulty = new FaultOnceMemory(memory, inventory + 0x28);
            error = Expand(faulty, chain, 0, 12, 160, current);
            check("仓库部分写入失败回退原尺寸", error != null && error.IndexOf("已恢复", StringComparison.Ordinal) >= 0
                && TryRead(memory, chain, 0, out address, out columns, out rows, out error) && columns == 10 && rows == 80);
            byte[] wrong = Encoding.ASCII.GetBytes("OtherData\0");
            memory.WriteBytes(name, wrong, 0, wrong.Length);
            check("仓库对象类型不符拒绝访问", !TryRead(memory, chain, 0, out address, out columns, out rows, out error));
        }

        private static void PutPointer(IMemory memory, ulong address, ulong value)
        {
            byte[] bytes = BitConverter.GetBytes(value); memory.WriteBytes(address, bytes, 0, bytes.Length);
        }

        private sealed class FaultOnceMemory : IMemory
        {
            private readonly IMemory inner;
            private readonly ulong target;
            private bool failed;
            public FaultOnceMemory(IMemory memory, ulong address) { inner = memory; target = address; }
            public bool ReadBytes(ulong address, byte[] bytes, int offset, int count)
            { return inner.ReadBytes(address, bytes, offset, count); }
            public bool WriteBytes(ulong address, byte[] bytes, int offset, int count)
            {
                if (address == target && !failed)
                { failed = true; inner.WriteBytes(address, bytes, offset, Math.Min(4, count)); return false; }
                return inner.WriteBytes(address, bytes, offset, count);
            }
        }
    }
}
