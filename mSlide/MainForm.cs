using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace MSlide;

public sealed class MainForm : Form
{
    private const int ThumbW = 240, ThumbH = 135; // 16:9 preview (ImageList max is 256)

    private readonly List<string> _slides = new();
    private readonly ListView _list;
    private readonly ImageList _thumbs;
    private readonly Label _hint;
    private readonly Label _status;
    private readonly Button _btnEarlier, _btnLater, _btnRemove, _btnClear, _btnSort, _btnSave;
    private readonly FlowLayoutPanel _toolbar;

    /// <summary>Payload for drags that start inside the list (reordering).</summary>
    private sealed record InternalDrag(int[] Indices);

    public MainForm()
    {
        Text = "mSlide";
        Font = new Font("Segoe UI", 10f);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(1040, 680);
        MinimumSize = new Size(640, 420);
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;

        _thumbs = new ImageList { ImageSize = new Size(ThumbW, ThumbH), ColorDepth = ColorDepth.Depth32Bit };

        _list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.LargeIcon,
            LargeImageList = _thumbs,
            MultiSelect = true,
            HideSelection = false,
            AllowDrop = true,
            BorderStyle = BorderStyle.None,
            BackColor = Color.FromArgb(245, 246, 248),
        };
        _list.ItemDrag += OnItemDrag;
        _list.DragEnter += OnDragOver;
        _list.DragOver += OnDragOver;
        _list.DragLeave += (_, _) => _list.InsertionMark.Index = -1;
        _list.DragDrop += OnDragDrop;
        _list.SelectedIndexChanged += (_, _) => UpdateButtons();

        _hint = new Label
        {
            Dock = DockStyle.Fill,
            AllowDrop = true,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 16f),
            ForeColor = Color.DimGray,
            BackColor = Color.FromArgb(245, 246, 248),
            Text = "Drag and drop PNG files here\r\n\r\n(or click \"Add Pictures…\")",
            Cursor = Cursors.Hand,
        };
        _hint.DragEnter += OnDragOver;
        _hint.DragOver += OnDragOver;
        _hint.DragDrop += OnDragDrop;
        _hint.Click += (_, _) => BrowseForFiles();

        var btnAdd = MakeButton("Add Pictures…", (_, _) => BrowseForFiles());
        _btnEarlier = MakeButton("◀ Move Earlier", (_, _) => MoveSelectedBy(-1));
        _btnLater = MakeButton("Move Later ▶", (_, _) => MoveSelectedBy(+1));
        _btnSort = MakeButton("Sort by Name", (_, _) => SortByName());
        _btnRemove = MakeButton("Remove", (_, _) => RemoveSelected());
        _btnClear = MakeButton("Clear All", (_, _) => ClearAll());
        _btnSave = MakeButton("Save as PowerPoint…", async (_, _) => await SaveAsync());
        _btnSave.Font = new Font(Font, FontStyle.Bold);

        _toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(8, 8, 8, 4),
            WrapContents = true,
        };
        _toolbar.Controls.AddRange(new Control[] { btnAdd, _btnEarlier, _btnLater, _btnSort, _btnRemove, _btnClear, _btnSave });

        _status = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 30,
            Padding = new Padding(10, 0, 10, 0),
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.DimGray,
        };

        Controls.Add(_list);
        Controls.Add(_hint);
        Controls.Add(_toolbar);
        Controls.Add(_status);

        RefreshList();
    }

    private Button MakeButton(string text, EventHandler onClick)
    {
        var b = new Button { Text = text, AutoSize = true, Padding = new Padding(8, 4, 8, 4), Margin = new Padding(4) };
        b.Click += onClick;
        return b;
    }

    // ------------------------------------------------------------------ adding

    private void BrowseForFiles()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Choose PNG pictures",
            Filter = "PNG pictures (*.png)|*.png",
            Multiselect = true,
        };
        if (dlg.ShowDialog(this) == DialogResult.OK)
            AddFiles(dlg.FileNames, _slides.Count);
    }

    /// <summary>Adds PNG files (and PNGs inside dropped folders) at the given position.</summary>
    public void AddFiles(IEnumerable<string> paths, int insertAt)
    {
        var candidates = new List<string>();
        foreach (var p in paths)
        {
            if (Directory.Exists(p))
                candidates.AddRange(Directory.EnumerateFiles(p, "*.png", SearchOption.TopDirectoryOnly));
            else if (File.Exists(p) && Path.GetExtension(p).Equals(".png", StringComparison.OrdinalIgnoreCase))
                candidates.Add(p);
        }

        // Explorer hands over dropped files in an unpredictable order, so sort them the way
        // Explorer displays names ("2.png" before "10.png").
        candidates.Sort(NaturalCompare);

        var added = new List<string>();
        var failed = new List<string>();
        UseWaitCursor = true;
        try
        {
            foreach (var path in candidates)
            {
                if (_thumbs.Images.ContainsKey(path)) { added.Add(path); continue; }
                try
                {
                    _thumbs.Images.Add(path, MakeThumbnail(path));
                    added.Add(path);
                }
                catch
                {
                    failed.Add(Path.GetFileName(path));
                }
            }
        }
        finally { UseWaitCursor = false; }

        insertAt = Math.Clamp(insertAt, 0, _slides.Count);
        _slides.InsertRange(insertAt, added);
        RefreshList(Enumerable.Range(insertAt, added.Count));

        int skipped = paths.Count() - candidates.Count;
        if (failed.Count > 0)
            MessageBox.Show(this, "These files could not be opened as pictures:\r\n\r\n" + string.Join("\r\n", failed),
                "mSlide", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        else if (added.Count == 0 && skipped > 0)
            MessageBox.Show(this, "Only PNG files can be added.", "mSlide", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private static Bitmap MakeThumbnail(string path)
    {
        using var fs = File.OpenRead(path);
        using var src = Image.FromStream(fs);
        var bmp = new Bitmap(ThumbW, ThumbH, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.Clear(Color.White);
        g.DrawImage(src, new Rectangle(0, 0, ThumbW, ThumbH)); // stretched, same as on the slide
        g.DrawRectangle(Pens.Silver, 0, 0, ThumbW - 1, ThumbH - 1);
        return bmp;
    }

    // ------------------------------------------------------------------ drag & drop

    private void OnItemDrag(object? sender, ItemDragEventArgs e)
    {
        var indices = _list.SelectedIndices.Cast<int>().OrderBy(i => i).ToArray();
        if (indices.Length == 0) return;
        _list.DoDragDrop(new InternalDrag(indices), DragDropEffects.Move);
        _list.InsertionMark.Index = -1;
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetDataPresent(typeof(InternalDrag)) == true)
            e.Effect = DragDropEffects.Move;
        else if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
            e.Effect = DragDropEffects.Copy;
        else
        {
            e.Effect = DragDropEffects.None;
            return;
        }

        if (sender != _list) return;

        var pt = _list.PointToClient(new Point(e.X, e.Y));
        ScrollWhileDragging(pt);
        int idx = _list.InsertionMark.NearestIndex(pt);
        if (idx >= 0)
        {
            var r = _list.GetItemRect(idx);
            _list.InsertionMark.AppearsAfterItem = pt.X > r.Left + r.Width / 2;
        }
        _list.InsertionMark.Index = idx;
    }

    private void OnDragDrop(object? sender, DragEventArgs e)
    {
        int target = _slides.Count;
        if (sender == _list && _list.InsertionMark.Index >= 0)
            target = _list.InsertionMark.Index + (_list.InsertionMark.AppearsAfterItem ? 1 : 0);
        _list.InsertionMark.Index = -1;

        if (e.Data?.GetData(typeof(InternalDrag)) is InternalDrag drag)
            MoveItems(drag.Indices, target);
        else if (e.Data?.GetData(DataFormats.FileDrop) is string[] files)
        {
            Activate();
            AddFiles(files, target);
        }
    }

    private void ScrollWhileDragging(Point pt)
    {
        const int edge = 40;
        if (pt.Y < edge) SendMessage(_list.Handle, WM_VSCROLL, (IntPtr)SB_LINEUP, IntPtr.Zero);
        else if (pt.Y > _list.ClientSize.Height - edge) SendMessage(_list.Handle, WM_VSCROLL, (IntPtr)SB_LINEDOWN, IntPtr.Zero);
    }

    // ------------------------------------------------------------------ reordering

    /// <summary>Moves the items at <paramref name="indices"/> so they land at insertion point <paramref name="target"/> (0..Count).</summary>
    private void MoveItems(int[] indices, int target)
    {
        var moving = indices.Select(i => _slides[i]).ToList();
        int newPos = target - indices.Count(i => i < target);
        foreach (int i in indices.OrderByDescending(i => i))
            _slides.RemoveAt(i);
        newPos = Math.Clamp(newPos, 0, _slides.Count);
        _slides.InsertRange(newPos, moving);
        RefreshList(Enumerable.Range(newPos, moving.Count));
    }

    private void MoveSelectedBy(int delta)
    {
        var sel = _list.SelectedIndices.Cast<int>().OrderBy(i => i).ToArray();
        if (sel.Length == 0) return;
        if (delta < 0 && sel[0] == 0) return;
        if (delta > 0 && sel[^1] == _slides.Count - 1) return;
        // Insertion point is in "before index" terms, so moving later needs +2 past the last item.
        int target = delta < 0 ? sel[0] - 1 : sel[^1] + 2;
        MoveItems(sel, target);
    }

    private void SortByName()
    {
        var selected = _list.SelectedIndices.Cast<int>().Select(i => _slides[i]).ToHashSet();
        _slides.Sort((a, b) => NaturalCompare(Path.GetFileName(a), Path.GetFileName(b)));
        RefreshList(_slides.Select((p, i) => (p, i)).Where(t => selected.Contains(t.p)).Select(t => t.i));
    }

    private void RemoveSelected()
    {
        var sel = _list.SelectedIndices.Cast<int>().OrderByDescending(i => i).ToArray();
        if (sel.Length == 0) return;
        foreach (int i in sel) _slides.RemoveAt(i);
        int next = Math.Min(sel[^1], _slides.Count - 1);
        RefreshList(next >= 0 ? new[] { next } : null);
    }

    private void ClearAll()
    {
        if (_slides.Count == 0) return;
        if (MessageBox.Show(this, "Remove all pictures from the list?", "mSlide",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        _slides.Clear();
        _list.Items.Clear();
        _thumbs.Images.Clear();
        RefreshList();
    }

    /// <summary>Syncs the ListView to <see cref="_slides"/> in place (keeps scroll position).</summary>
    private void RefreshList(IEnumerable<int>? select = null)
    {
        var selSet = select?.ToHashSet() ?? new HashSet<int>();
        _list.BeginUpdate();
        while (_list.Items.Count > _slides.Count) _list.Items.RemoveAt(_list.Items.Count - 1);
        while (_list.Items.Count < _slides.Count) _list.Items.Add(new ListViewItem());
        for (int i = 0; i < _slides.Count; i++)
        {
            var item = _list.Items[i];
            string text = $"{i + 1}.  {Path.GetFileName(_slides[i])}";
            if (item.Text != text) item.Text = text;
            if (item.ImageKey != _slides[i]) item.ImageKey = _slides[i];
            item.ToolTipText = _slides[i];
            if (select != null) item.Selected = selSet.Contains(i);
        }
        _list.EndUpdate();

        if (selSet.Count > 0) _list.EnsureVisible(selSet.Max());
        if (selSet.Count > 0) _list.EnsureVisible(selSet.Min());

        bool empty = _slides.Count == 0;
        _hint.Visible = empty;
        _list.Visible = !empty;
        _status.Text = empty
            ? "No slides yet."
            : $"{_slides.Count} slide{(_slides.Count == 1 ? "" : "s")}  —  drag pictures to reorder; each one will be stretched to fill a 16:9 slide.";
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        var sel = _list.SelectedIndices.Cast<int>().ToArray();
        bool any = sel.Length > 0;
        _btnEarlier.Enabled = any && sel.Min() > 0;
        _btnLater.Enabled = any && sel.Max() < _slides.Count - 1;
        _btnRemove.Enabled = any;
        _btnClear.Enabled = _slides.Count > 0;
        _btnSort.Enabled = _slides.Count > 1;
        _btnSave.Enabled = _slides.Count > 0;
    }

    // ------------------------------------------------------------------ keyboard

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (_list.Focused || ActiveControl == _list)
        {
            switch (keyData)
            {
                case Keys.Delete: RemoveSelected(); return true;
                case Keys.Control | Keys.A:
                    _list.BeginUpdate();
                    foreach (ListViewItem it in _list.Items) it.Selected = true;
                    _list.EndUpdate();
                    return true;
                case Keys.Control | Keys.Left:
                case Keys.Control | Keys.Up: MoveSelectedBy(-1); return true;
                case Keys.Control | Keys.Right:
                case Keys.Control | Keys.Down: MoveSelectedBy(+1); return true;
            }
        }
        if (keyData == (Keys.Control | Keys.S) && _btnSave.Enabled) { _btnSave.PerformClick(); return true; }
        if (keyData == (Keys.Control | Keys.O)) { BrowseForFiles(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    // ------------------------------------------------------------------ saving

    private async Task SaveAsync()
    {
        if (_slides.Count == 0) return;

        using var dlg = new SaveFileDialog
        {
            Title = "Save PowerPoint",
            Filter = "PowerPoint Presentation (*.pptx)|*.pptx",
            DefaultExt = "pptx",
            AddExtension = true,
            FileName = "Slides.pptx",
            OverwritePrompt = true,
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        string outPath = dlg.FileName;
        var slides = _slides.ToList();
        int total = slides.Count;
        var progress = new Progress<int>(k => _status.Text = $"Saving… slide {k} of {total}");

        _toolbar.Enabled = false;
        _list.Enabled = false;
        UseWaitCursor = true;
        try
        {
            await Task.Run(() => PptxWriter.Write(outPath, slides, progress));
        }
        catch (IOException ex)
        {
            MessageBox.Show(this, "Could not save the file. If it is open in PowerPoint, close it and try again.\r\n\r\n" + ex.Message,
                "mSlide", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Could not save the file.\r\n\r\n" + ex.Message, "mSlide", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        finally
        {
            UseWaitCursor = false;
            _toolbar.Enabled = true;
            _list.Enabled = true;
            RefreshList();
        }

        if (MessageBox.Show(this, $"Saved {total} slide{(total == 1 ? "" : "s")} to:\r\n{outPath}\r\n\r\nOpen it now?",
                "mSlide", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
        {
            try { Process.Start(new ProcessStartInfo(outPath) { UseShellExecute = true }); }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not open the file:\r\n" + ex.Message, "mSlide", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }

    // ------------------------------------------------------------------ native helpers

    private const int WM_VSCROLL = 0x0115, SB_LINEUP = 0, SB_LINEDOWN = 1;

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int StrCmpLogicalW(string x, string y);

    private static int NaturalCompare(string a, string b) => StrCmpLogicalW(a, b);
}
