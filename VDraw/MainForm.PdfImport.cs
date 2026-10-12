using SkiaSharp;

using VDraw.Documents;
using VDraw.Shapes;

namespace VDraw;

/// <summary>
/// Импорт PDF. Вынесен в отдельный файл частичного класса: логика
/// самодостаточна, а MainForm.cs и без того на четыре тысячи строк.
/// </summary>
public partial class MainForm
{
	/// <summary>
	/// Выбирает страницы и вставляет их в документ.
	///
	/// Одностраничный файл импортируется сразу: спрашивать «какую из одной
	/// страницы вы хотите» — лишний щелчок без выбора.
	/// </summary>
	private async void ImportPdfDocument(string filePath)
	{
		if (CurrentPage == null || _mainView == null)
			return;

		List<SKSize> sizes;

		try
		{
			Cursor = Cursors.WaitCursor;

			// Чтение размеров не разбирает содержимое страниц, но открывает
			// файл целиком — на сотне мегабайт это заметно, поэтому в фоне.
			sizes = await Task.Run(() => PdfImporter.ReadPageSizesMm(filePath));
		}
		catch (UnauthorizedAccessException ex)
		{
			MessageBox.Show(this, ex.Message, "Import PDF", MessageBoxButtons.OK, MessageBoxIcon.Warning);
			return;
		}
		catch (Exception ex)
		{
			ShowPdfError(ex);
			return;
		}
		finally
		{
			Cursor = Cursors.Default;
		}

		if (sizes.Count == 0)
		{
			MessageBox.Show(this, "The PDF contains no pages.", "Import PDF");
			return;
		}

		List<int> pages;
		bool ownSheets;

		if (sizes.Count == 1)
		{
			pages = new List<int> { 0 };
			ownSheets = false;
		}
		else
		{
			using var selector = new PdfPageSelectForm(filePath, sizes);

			if (selector.ShowDialog(this) != DialogResult.OK || selector.SelectedPages.Count == 0)
				return;

			pages = selector.SelectedPages;
			ownSheets = selector.EachPageOnOwnSheet && pages.Count > 1;
		}

		await RunPdfImport(filePath, sizes, pages, ownSheets);
	}

	private async Task RunPdfImport(
		string filePath, IReadOnlyList<SKSize> sizes, List<int> pages, bool ownSheets)
	{
		var warnings = new List<string>();
		var fontReport = new PdfFontReport();

		int totalShapes = 0;
		Page? firstCreated = null;

		try
		{
			Cursor = Cursors.WaitCursor;

			foreach (int index in pages)
			{
				// Лист создаётся до разбора: импортёр вписывает страницу PDF
				// в тот лист, куда её кладут, и размеры нужны ему заранее.
				Page target = ownSheets
					? CreateSheetForPdfPage(sizes[index], index)
					: CurrentPage!;

				firstCreated ??= ownSheets ? target : null;

				var options = new PdfImportOptions
				{
					Mode = PdfImportMode.VectorWithRasterFallback,
					RasterDpi = 300f,

					// Лист уже создан под размер страницы, ужимать нечего.
					FitToPage = !ownSheets
				};

				options.Pages.Add(index);

				// Разбор в фоне: страница чертежа на десять тысяч контуров
				// считается секунды, и на потоке интерфейса это выглядит
				// зависанием приложения.
				var result = await Task.Run(() => PdfImporter.Import(filePath, target, options));

				if (result.Shapes.Count > 0)
				{
					target.AddShapes(result.Shapes);
					totalShapes += result.Shapes.Count;
				}

				warnings.AddRange(result.Warnings);
				fontReport.Fonts.AddRange(result.Fonts.Fonts);
			}
		}
		catch (Exception ex)
		{
			ShowPdfError(ex);
			return;
		}
		finally
		{
			Cursor = Cursors.Default;
		}

		if (totalShapes == 0)
		{
			MessageBox.Show(this, "The PDF contains no importable content.", "Import PDF");
			return;
		}

		cur_doc.need_save = true;

		if (firstCreated != null)
		{
			_pageTabs?.SetDocument(cur_doc);
			OnPageTabActivated(firstCreated);
		}
		else
		{
			sel.NotifyGeometryChanged();
			_mainView?.Invalidate();
		}

		ReportPdfFonts(fontReport);

		if (warnings.Count > 0)
		{
			MessageBox.Show(
				this,
				string.Join(Environment.NewLine, warnings.Distinct().Take(10)),
				"Import notes",
				MessageBoxButtons.OK,
				MessageBoxIcon.Information);
		}
	}

	/// <summary>
	/// Новый лист под размер страницы PDF. Формат берётся у текущего листа,
	/// меняются только габариты и ориентация: единицы, разрешение и цвет
	/// фона должны остаться теми, к которым привык документ.
	/// </summary>
	private Page CreateSheetForPdfPage(SKSize sizeMm, int pdfPageIndex)
	{
		var template = CurrentPage!;

		var page = new Page(
			template.paper_format!,
			template.uom!,
			template.DPI,
			template.size_uom.Width,
			template.size_uom.Height,
			template.Orientation,
			cur_doc);

		page.SetFormatData(template.ClonePaperFormatData());

		float mmPerUom = MathF.Max(template.MmPerUom, 0.0001f);

		float widthUom = sizeMm.Width / mmPerUom;
		float heightUom = sizeMm.Height / mmPerUom;

		if (widthUom > 0.01f && heightUom > 0.01f)
		{
			page.size_uom = new SKSize(widthUom, heightUom);

			page.Orientation = widthUom > heightUom
				? PageOrientation.Horizontal
				: PageOrientation.Vertical;
		}

		page.title = $"PDF {pdfPageIndex + 1}";

		cur_doc.AddPage(page);

		return page;
	}

	/// <summary>
	/// Оповещение о шрифтах. Показывается, только если есть о чём сказать:
	/// на документе, все шрифты которого нашлись, окно не появляется.
	/// </summary>
	private void ReportPdfFonts(PdfFontReport report)
	{
		string message = report.BuildMessage();

		if (string.IsNullOrEmpty(message))
			return;

		// Список семейств мог пополниться поднятыми из файла шрифтами,
		// а он кэшируется на всё время работы приложения.
		SystemFontCatalog.InvalidateFamilies();

		MessageBox.Show(
			this,
			message,
			report.HasMissing ? "Отсутствующие шрифты" : "Шрифты документа",
			MessageBoxButtons.OK,
			report.HasMissing ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
	}

	private void ShowPdfError(Exception ex)
	{
		MessageBox.Show(
			this,
			$"Failed to import PDF.\r\n\r\n{ex.Message}",
			"Import error",
			MessageBoxButtons.OK,
			MessageBoxIcon.Error);
	}
}
