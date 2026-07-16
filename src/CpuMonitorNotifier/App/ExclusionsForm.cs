using System.Diagnostics;
using CpuMonitorNotifier.Localization;

namespace CpuMonitorNotifier.App;

/// <summary>Редактор списка процессов-исключений: их нагрузка не вызывает уведомлений.</summary>
internal sealed class ExclusionsForm : Form
{
    private readonly ListBox _list;
    private readonly ComboBox _picker;

    /// <summary>Итоговый список имён процессов (заполняется при OK).</summary>
    public List<string> Result { get; } = new();

    public ExclusionsForm(IEnumerable<string> current)
    {
        Text = string.Format(Loc.T("exclusions.title"), Loc.AppName);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(360, 320);
        try { Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 4,
            Padding = new Padding(12),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

        var hint = new Label { Text = Loc.T("exclusions.hint"), Dock = DockStyle.Fill, AutoSize = false };
        layout.Controls.Add(hint, 0, 0);
        layout.SetColumnSpan(hint, 2);

        _picker = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDown };
        _picker.Items.AddRange(RunningProcessNames());
        layout.Controls.Add(_picker, 0, 1);
        var addBtn = new Button { Text = Loc.T("exclusions.add"), AutoSize = true, Anchor = AnchorStyles.Left };
        addBtn.Click += (_, _) => AddName();
        layout.Controls.Add(addBtn, 1, 1);

        _list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
        _list.Items.AddRange(current.Distinct(StringComparer.OrdinalIgnoreCase).Cast<object>().ToArray());
        layout.Controls.Add(_list, 0, 2);
        var removeBtn = new Button { Text = Loc.T("exclusions.remove"), AutoSize = true, Anchor = AnchorStyles.Top | AnchorStyles.Left };
        removeBtn.Click += (_, _) => { if (_list.SelectedItem is string s) _list.Items.Remove(s); };
        layout.Controls.Add(removeBtn, 1, 2);

        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, AutoSize = true };
        var ok = new Button { Text = Loc.T("settings.ok"), DialogResult = DialogResult.OK };
        var cancel = new Button { Text = Loc.T("settings.cancel"), DialogResult = DialogResult.Cancel };
        ok.Click += (_, _) => { foreach (var it in _list.Items) Result.Add((string)it); };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);
        layout.Controls.Add(buttons, 0, 3);
        layout.SetColumnSpan(buttons, 2);

        Controls.Add(layout);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    private void AddName()
    {
        string name = _picker.Text.Trim();
        if (name.Length == 0)
            return;
        if (!_list.Items.Cast<string>().Any(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase)))
            _list.Items.Add(name);
        _picker.Text = string.Empty;
    }

    private static object[] RunningProcessNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Process.GetProcesses())
        {
            try { if (!string.IsNullOrEmpty(p.ProcessName)) names.Add(p.ProcessName); }
            catch { /* процесс мог завершиться */ }
            finally { p.Dispose(); }
        }
        return names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).Cast<object>().ToArray();
    }
}
