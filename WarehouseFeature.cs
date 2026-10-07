using System;
using System.Globalization;
using System.Windows.Forms;

namespace LilliaTrainer
{
    internal sealed partial class MainForm
    {
        private ComboBox cboWarehouseKind;
        private TextBox txtWarehouseColumns, txtWarehouseRows;
        private Label lblWarehouseSize;
        private Button btnWarehouseExpand;
        private Gen warehouseDisplayedGen;

        private void AddWarehouseControls(FlowLayoutPanel panel)
        {
            cboWarehouseKind = Cbo(124, new string[] { "仓库 / 服装", "消耗品仓库", "杂物仓库" }, 0);
            txtWarehouseColumns = Num(60, "10");
            txtWarehouseRows = Num(60, "160");
            lblWarehouseSize = CardLabel("当前容量尚未读取；应用后请在游戏内保存，再重新载入该存档刷新仓库网格。", 730);
            lblWarehouseSize.Height = 38;
            cboWarehouseKind.SelectedIndexChanged += delegate(object s, EventArgs e) { RefreshWarehouseCapacity(true); };
            btnWarehouseExpand = Btn("应用扩容", 82, delegate(object s, EventArgs e) { ApplyWarehouseExpansion(); });
            btnWarehouseExpand.Enabled = false;
            Panel row = Row(32, Lbl("仓库扩容", 64, true), cboWarehouseKind,
                Lbl("列数", 36, false), txtWarehouseColumns, Lbl("行数", 36, false), txtWarehouseRows,
                btnWarehouseExpand, Btn("刷新容量", 82, delegate(object s, EventArgs e) { RefreshWarehouseCapacity(true); }));
            row.Width = 730;
            panel.Controls.Add(row);
            Panel info = Row(40, lblWarehouseSize);
            info.Width = 730;
            panel.Controls.Add(info);
        }

        private void UpdateWarehouseEnabled(bool connected)
        {
            if (btnWarehouseExpand == null) return;
            btnWarehouseExpand.Enabled = connected && !PreviewOnly && !outfitBusy;
            if (!connected)
            {
                warehouseDisplayedGen = null;
                lblWarehouseSize.Text = "仓库容量尚未读取；请连接游戏并载入存档。";
            }
        }

        private void RefreshWarehouseIfStale(Snapshot snapshot)
        {
            if (warehouseDisplayedGen == null
                || !warehouseDisplayedGen.Same(snapshot.Pid, ProcStart(), snapshot.Holder, snapshot.Save))
                RefreshWarehouseCapacity(true);
        }

        private void RefreshWarehouseCapacity(bool fillTarget)
        {
            if (lblWarehouseSize == null) return;
            if (PreviewOnly)
            {
                lblWarehouseSize.Text = "预览：当前 10 列 × 80 行 = 800 格；上限每边 256、总计 4096 格。\n应用后请在游戏内保存，再重新载入该存档刷新仓库网格。";
                return;
            }
            Chain chain; string error; ulong address; int columns, rows;
            if (!FreshChain(out chain, out error)
                || !WarehouseCapacity.TryRead(mem, chain, cboWarehouseKind.SelectedIndex, out address, out columns, out rows, out error))
            {
                warehouseDisplayedGen = null;
                lblWarehouseSize.Text = "仓库容量不可用：" + error;
                return;
            }
            warehouseDisplayedGen = new Gen(pid, ProcStart(), chain.Holder, chain.Save);
            long requiredColumns, requiredRows; string layoutError;
            bool layoutReadable = WarehouseCapacity.TryGetRequiredSize(mem, chain, cboWarehouseKind.SelectedIndex,
                out requiredColumns, out requiredRows, out layoutError);
            lblWarehouseSize.Text = "当前 " + columns + " 列 × " + rows + " 行 = " + columns * rows
                + (layoutReadable ? " 格；物品至少需 " + requiredColumns + " 列 × " + requiredRows + " 行。" : " 格；" + layoutError)
                + "\n上限每边 256、总计 4096 格；应用后请在游戏内保存并重新载入。";
            if (fillTarget)
            {
                int targetColumns = columns;
                if (layoutReadable && requiredColumns <= WarehouseCapacity.MaxAxis && requiredRows <= WarehouseCapacity.MaxAxis
                    && Math.Max((long)columns, requiredColumns) * Math.Max((long)rows, requiredRows) <= WarehouseCapacity.MaxCells)
                    targetColumns = (int)Math.Max(columns, requiredColumns);
                txtWarehouseColumns.Text = targetColumns.ToString(CultureInfo.InvariantCulture);
                int suggested = Math.Min(WarehouseCapacity.MaxAxis,
                    Math.Min(rows * 2, WarehouseCapacity.MaxCells / targetColumns));
                int targetRows = Math.Max(rows, suggested);
                if (layoutReadable && requiredRows <= WarehouseCapacity.MaxAxis
                    && (long)targetColumns * requiredRows <= WarehouseCapacity.MaxCells)
                    targetRows = (int)Math.Max(targetRows, requiredRows);
                txtWarehouseRows.Text = targetRows.ToString(CultureInfo.InvariantCulture);
            }
        }

        private void ApplyWarehouseExpansion()
        {
            if (PreviewOnly || closing) return;
            if (outfitBusy) { lblWarehouseSize.Text = "请等待换装任务结束后再扩容。"; return; }
            int columns, rows;
            if (!int.TryParse(txtWarehouseColumns.Text.Trim(), out columns)
                || !int.TryParse(txtWarehouseRows.Text.Trim(), out rows))
            { lblWarehouseSize.Text = "仓库列数和行数必须是整数"; SetStatus(lblWarehouseSize.Text, false); return; }
            int kind = cboWarehouseKind.SelectedIndex;
            Gen expected = warehouseDisplayedGen;
            string failure = null;
            bool applied = Mutate("仓库扩容", delegate(IMemory memory, Chain chain)
            {
                if (!WritePolicy.Current(expected, pid, ProcStart(), chain))
                    return failure = "仓库信息已过期，请先刷新容量";
                return failure = WarehouseCapacity.Expand(memory, chain, kind, columns, rows, delegate
                {
                    Chain fresh; string error;
                    return FreshChain(out fresh, out error) && object.ReferenceEquals(memory, mem)
                        && WritePolicy.Current(expected, pid, ProcStart(), fresh);
                });
            });
            if (applied)
            {
                RefreshWarehouseCapacity(true);
                SetStatus("已扩容为 " + columns + " × " + rows + "；请在游戏内保存，再重新载入存档刷新网格", true);
            }
            else if (failure != null) lblWarehouseSize.Text = "扩容未完成：" + failure;
        }

        internal void WarehouseSafetySelfTest(Action<string, bool> check)
        {
            check("仓库：预览不能点击扩容", PreviewOnly && !btnWarehouseExpand.Enabled);
            ApplyWarehouseExpansion();
            check("仓库：预览调用不连接游戏", mem == null && warehouseDisplayedGen == null);
        }
    }
}
