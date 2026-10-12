using SkiaSharp;

using VDraw.Documents;

namespace VDraw;

/// <summary>
/// Выбор страниц импортируемого PDF.
///
/// Миниатюры рисуются в фоне и появляются по мере готовности: на
/// стостраничном документе отрисовка всех превью до показа окна выглядела
/// бы как зависание после выбора файла. Отрисовка отменяется при закрытии
/// окна, иначе фоновая задача продолжала бы держать блокировку PDFium
/// и следующий импорт ждал бы её впустую.
/// </summary>
public sealed class PdfPageSelectForm : Form
{
	private const int ThumbWidth = 132;
	private const int ThumbHeight = 176;

	private readonly ListView _list;
	private readonly ImageList _images;
	private readonly CheckBox _ownSheets;
	private readonly Label _hint;

	private readonly string _filePath;
	private readonly string? _password;
	private readonly int _pageCount;

	private CancellationTokenSource? _cancel;

	/// <summary>Номера выбранных страниц, с нуля.</summary>
	public List<int> SelectedPages { get; } = new();

	/// <summary>Каждую выбранную страницу — на отдельный лист документа.</summary>
	public bool EachPageOnOwnSheet => _ownSheets.Checked;

	public PdfPageSelectForm(string filePath, IReadOnlyList<SKSize> pageSizesMm, string? password = null)
	{
		WinFormsTheme.Register(this);
		_filePath = filePath;
		_password = password;
		_pageCount = pageSizesMm.Count;

		Text = "Import PDF — select pages";
		StartPosition = FormStartPosition.CenterParent;
		MinimizeBox = false;
		MaximizeBox = false;
		ShowInTaskbar = false;
		FormBorderStyle = FormBorderStyle.Sizable;
		ClientSize = new Size(680, 520);
		MinimumSize = new Size(520, 400);

		_images = new ImageList
		{
			ImageSize = new Size(ThumbWidth, ThumbHeight),
			ColorDepth = ColorDepth.Depth32Bit
		};

		_list = new ListView
		{
			Dock = DockStyle.Fill,
			View = View.LargeIcon,
			MultiSelect = true,
			HideSelection = false,
			LargeImageList = _images,
			BorderStyle = BorderStyle.FixedSingle
		};

		_list.SelectedIndexChanged += (_, _) => UpdateState();
		_list.ItemActivate += (_, _) => Accept();

		for (int i = 0; i < _pageCount; i++)
		{
			var size = pageSizesMm[i];

			string caption = size.Width > 0 && size.Height > 0
				? $"{i + 1}   {size.Width:0}×{size.Height:0} мм"
				: $"{i + 1}";

			_list.Items.Add(new ListViewItem(caption) { Tag = i });
		}

		if (_list.Items.Count > 0)
			_list.Items[0].Selected = true;

		_ownSheets = new CheckBox
		{
			Text = "Каждую страницу на отдельный лист",
			Checked = true,
			AutoSize = true,
			Location = new Point(12, 12)
		};

		_ownSheets.CheckedChanged += (_, _) => UpdateState();

		_hint = new Label
		{
			AutoSize = true,
			ForeColor = SystemColors.GrayText,
			Location = new Point(12, 36)
		};

		var selectAll = new Button
		{
			Text = "Все",
			Width = 80,
			Height = 28,
			Anchor = AnchorStyles.Top | AnchorStyles.Right
		};

		var ok = new Button
		{
			Text = "Импорт",
			DialogResult = DialogResult.OK,
			Width = 100,
			Height = 28,
			Anchor = AnchorStyles.Top | AnchorStyles.Right
		};

		var cancel = new Button
		{
			Text = "Отмена",
			DialogResult = DialogResult.Cancel,
			Width = 100,
			Height = 28,
			Anchor = AnchorStyles.Top | AnchorStyles.Right
		};

		selectAll.Click += (_, _) =>
		{
			foreach (ListViewItem item in _list.Items)
				item.Selected = true;

			_list.Focus();
		};

		ok.Click += (_, _) => Accept();

		var bottom = new Panel
		{
			Dock = DockStyle.Bottom,
			Height = 74
		};

		bottom.Controls.AddRange(new Control[] { _ownSheets, _hint, selectAll, ok, cancel });

		bottom.Resize += (_, _) =>
		{
			int right = bottom.ClientSize.Width - 12;

			cancel.Location = new Point(right - cancel.Width, 20);
			ok.Location = new Point(cancel.Left - ok.Width - 8, 20);
			selectAll.Location = new Point(ok.Left - selectAll.Width - 24, 20);
		};

		var host = new Panel
		{
			Dock = DockStyle.Fill,
			Padding = new Padding(12, 12, 12, 0)
		};

		host.Controls.Add(_list);

		Controls.Add(host);
		Controls.Add(bottom);

		AcceptButton = ok;
		CancelButton = cancel;

		UpdateState();
	}

	protected override void OnShown(EventArgs e)
	{
		base.OnShown(e);

		StartThumbnails();
	}

	protected override void OnFormClosed(FormClosedEventArgs e)
	{
		_cancel?.Cancel();

		base.OnFormClosed(e);
	}

	private void Accept()
	{
		SelectedPages.Clear();

		foreach (ListViewItem item in _list.SelectedItems)
			SelectedPages.Add((int)item.Tag!);

		if (SelectedPages.Count == 0)
			return;

		SelectedPages.Sort();

		DialogResult = DialogResult.OK;
		Close();
	}

	private void UpdateState()
	{
		int selected = _list.SelectedItems.Count;

		_ownSheets.Enabled = selected > 1;

		_hint.Text = selected switch
		{
			0 => "Выберите хотя бы одну страницу.",
			1 => "Страница будет вставлена в текущий лист.",
			_ when _ownSheets.Checked => $"Будет создано листов: {selected}.",
			_ => $"Все {selected} страницы лягут в текущий лист друг поверх друга."
		};

	}

	#region Миниатюры

	private void StartThumbnails()
	{
		_cancel = new CancellationTokenSource();

		var token = _cancel.Token;
		var indices = Enumerable.Range(0, _pageCount).ToList();

		Task.Run(() =>
		{
			try
			{
				PdfImporter.RenderPages(
					_filePath, indices, Math.Max(ThumbWidth, ThumbHeight) - 12, _password,
					(index, bitmap) => OnThumbnailReady(index, bitmap, token),
					token);
			}
			catch
			{
				// Предпросмотр — удобство. Список страниц уже построен
				// по размерам, и выбрать страницу можно без миниатюр.
			}
		}, token);
	}

	private void OnThumbnailReady(int index, SKBitmap? bitmap, CancellationToken token)
	{
		if (bitmap == null)
			return;

		Image? image = null;

		try
		{
			image = ToThumbnail(bitmap);
		}
		catch
		{
		}
		finally
		{
			bitmap.Dispose();
		}

		if (image == null)
			return;

		if (token.IsCancellationRequested || IsDisposed || !IsHandleCreated)
		{
			image.Dispose();
			return;
		}

		try
		{
			BeginInvoke(() =>
			{
				if (IsDisposed || index >= _list.Items.Count)
				{
					image.Dispose();
					return;
				}

				_images.Images.Add(image);

				_list.Items[index].ImageIndex = _images.Images.Count - 1;
			});
		}
		catch (ObjectDisposedException)
		{
			image.Dispose();
		}
		catch (InvalidOperationException)
		{
			// Окно закрылось между проверкой и вызовом.
			image.Dispose();
		}
	}

	/// <summary>
	/// Приводит страницу к общему размеру ячейки: ImageList требует
	/// одинаковых картинок, а страницы бывают и книжные, и альбомные.
	/// </summary>
	private static Image ToThumbnail(SKBitmap source)
	{
		using var skImage = SKImage.FromBitmap(source);
		using var data = skImage.Encode(SKEncodedImageFormat.Png, 90);
		using var stream = new MemoryStream(data.ToArray());
		using var decoded = new Bitmap(stream);

		var canvas = new Bitmap(ThumbWidth, ThumbHeight);

		using (var g = Graphics.FromImage(canvas))
		{
			g.Clear(Color.Transparent);
			g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;

			float k = Math.Min(
				(ThumbWidth - 8f) / decoded.Width,
				(ThumbHeight - 8f) / decoded.Height);

			int w = Math.Max(1, (int)(decoded.Width * k));
			int h = Math.Max(1, (int)(decoded.Height * k));

			int x = (ThumbWidth - w) / 2;
			int y = (ThumbHeight - h) / 2;

			g.FillRectangle(Brushes.White, x, y, w, h);
			g.DrawImage(decoded, x, y, w, h);
			g.DrawRectangle(Pens.Silver, x, y, w - 1, h - 1);
		}

		return canvas;
	}

	#endregion
}
