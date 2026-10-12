using SkiaSharp;

using System.Globalization;

using VDraw.Shapes;

namespace VDraw.Documents;

/// <summary>
/// Превращение объектов страницы PDF в фигуры VDraw.
///
/// Обход рекурсивный: Form XObject — это вложенный поток со своей
/// матрицей, и его содержимое обязано получить произведение матриц
/// всех охватывающих форм. Разворачивать формы в вызывающем коде нельзя:
/// вложенность бывает пятиуровневой, и накопление матрицы там пришлось бы
/// повторить.
///
/// Кривые PDF — только кубические Безье: ни квадратичных, ни дуг в
/// формате нет. Поэтому отображение на CurveNode точное, без
/// аппроксимации: контрольная точка сегмента становится ControlNext
/// начального узла и ControlPrev конечного.
/// </summary>
internal static class PdfShapeBuilder
{
	/// <summary>Глубина вложенности форм, дальше которой разбор прекращается.</summary>
	private const int MaxFormDepth = 12;

	/// <summary>Толщина линии нулевой ширины: в PDF это «тончайшая видимая».</summary>
	private const float HairlineMm = 0.05f;

	private sealed class BuildContext
	{
		public required PdfDocument Doc;
		public required IntPtr Page;
		public required IntPtr TextPage;
		public required PdfPageMap Map;
		public required PdfImportOptions Options;
		public required PdfImportResult Result;
		public required Page TargetPage;
		public required List<Shape> Shapes;
		public required PdfFontResolver Fonts;
		public required PdfLayerAllocator Layers;

		public bool ShadingWarned;
		public bool LimitHit;
	}

	#region Точка входа

	public static List<Shape> BuildPage(
		PdfDocument doc, IntPtr page, in PdfPageMap map,
		PdfImportOptions options, PdfImportResult result, Page targetPage,
		PdfFontResolver fonts, PdfLayerAllocator layers)
	{
		var shapes = new List<Shape>();

		// Текстовая страница нужна только чтобы вытащить строки объектов.
		// Загружается один раз: на каждый объект — это заметная доля
		// времени импорта на текстовых PDF.
		IntPtr textPage = options.TextAsText ? PdfNative.FPDFText_LoadPage(page) : IntPtr.Zero;

		var context = new BuildContext
		{
			Doc = doc,
			Page = page,
			TextPage = textPage,
			Map = map,
			Options = options,
			Result = result,
			TargetPage = targetPage,
			Shapes = shapes,
			Fonts = fonts,
			Layers = layers
		};

		try
		{
			int count = PdfNative.FPDFPage_CountObjects(page);

			for (int i = 0; i < count; i++)
			{
				if (context.LimitHit)
					break;

				IntPtr obj = PdfNative.FPDFPage_GetObject(page, i);

				if (obj != IntPtr.Zero)
					VisitObject(context, obj, map.ToPage, 0);
			}
		}
		finally
		{
			if (textPage != IntPtr.Zero)
				PdfNative.FPDFText_ClosePage(textPage);
		}

		return shapes;
	}

	#endregion

	#region Обход

	private static void VisitObject(BuildContext ctx, IntPtr obj, SKMatrix parent, int depth)
	{
		if (ctx.Shapes.Count + ctx.Result.Shapes.Count >= ctx.Options.MaxShapes)
		{
			ctx.LimitHit = true;
			ctx.Result.LimitReached = true;
			return;
		}

		int type = PdfNative.FPDFPageObj_GetType(obj);

		// Матрица объекта применяется всегда. Для контуров, разобранных
		// из потока, PDFium обычно возвращает единичную — тогда умножение
		// безвредно; для картинок и текста она несёт всю привязку к месту.
		SKMatrix full = PdfNative.FPDFPageObj_GetMatrix(obj, out var m)
			? Multiply(parent, FromPdf(m.a, m.b, m.c, m.d, m.e, m.f))
			: parent;

		switch (type)
		{
			case PdfNative.FPDF_PAGEOBJ_PATH:
				{
					Emit(ctx, BuildPath(ctx, obj, full));
					break;
				}

			case PdfNative.FPDF_PAGEOBJ_TEXT:
				{
					Emit(ctx, BuildText(ctx, obj, full));
					break;
				}

			case PdfNative.FPDF_PAGEOBJ_IMAGE:
				{
					if (!ctx.Options.ImportImages)
						break;

					Emit(ctx, BuildImage(ctx, obj, full));
					break;
				}

			case PdfNative.FPDF_PAGEOBJ_FORM:
				{
					if (!ctx.Options.FlattenForms || depth >= MaxFormDepth)
						break;

					int childCount = PdfNative.FPDFFormObj_CountObjects(obj);

					for (int i = 0; i < childCount; i++)
					{
						if (ctx.LimitHit)
							break;

						IntPtr child = PdfNative.FPDFFormObj_GetObject(obj, (uint)i);

						if (child != IntPtr.Zero)
							VisitObject(ctx, child, full, depth + 1);
					}

					break;
				}

			case PdfNative.FPDF_PAGEOBJ_SHADING:
				{
					// Градиентные заливки типов 4–7 (сетчатые и координатные)
					// не имеют представления в модели VDraw. Пропускаем
					// с одним предупреждением на страницу, а не на объект.
					if (!ctx.ShadingWarned)
					{
						ctx.ShadingWarned = true;
						ctx.Result.Warnings.Add(
							"Сетчатые градиенты (Shading) пропущены: в модели заливок VDraw их нет. " +
							"Для точной картинки используйте растровый режим.");
					}

					break;
				}
		}
	}

	/// <summary>
	/// Выпускает готовую фигуру, выдавая ей слой.
	///
	/// Единственное место, где назначается слой: номер должен расти строго
	/// в порядке отрисовки PDF, а объекты приходят из трёх разных
	/// построителей и из рекурсии по формам.
	/// </summary>
	private static void Emit(BuildContext ctx, Shape? shape)
	{
		if (shape == null)
			return;

		shape.Layer = ctx.Layers.Next();

		ctx.Shapes.Add(shape);
	}

	#endregion

	#region Контуры

	private static CurveShape? BuildPath(BuildContext ctx, IntPtr obj, in SKMatrix full)
	{
		int segments = PdfNative.FPDFPath_CountSegments(obj);

		if (segments <= 0)
			return null;

		if (!PdfNative.FPDFPath_GetDrawMode(obj, out int fillMode, out bool stroked))
		{
			fillMode = PdfNative.FPDF_FILLMODE_NONE;
			stroked = false;
		}

		bool filled = fillMode != PdfNative.FPDF_FILLMODE_NONE;

		if (!filled && !stroked && ctx.Options.SkipInvisible)
			return null;

		var shape = new CurveShape
		{
			Name = "PDF path",
			FillType = fillMode == PdfNative.FPDF_FILLMODE_ALTERNATE
				? SKPathFillType.EvenOdd
				: SKPathFillType.Winding
		};

		// Незакрытая тройка контрольных точек кривой: PDF выдаёт их
		// тремя отдельными сегментами подряд.
		SKPoint? pendingC1 = null;
		SKPoint? pendingC2 = null;

		int contourStart = -1;
		bool contourOpen = false;

		for (int i = 0; i < segments; i++)
		{
			IntPtr seg = PdfNative.FPDFPath_GetPathSegment(obj, i);

			if (seg == IntPtr.Zero)
				continue;

			if (!PdfNative.FPDFPathSegment_GetPoint(seg, out float x, out float y))
				continue;

			var p = full.MapPoint(x, y);

			int segType = PdfNative.FPDFPathSegment_GetType(seg);
			bool closes = PdfNative.FPDFPathSegment_GetClose(seg);

			switch (segType)
			{
				case PdfNative.FPDF_SEGMENT_MOVETO:
					{
						pendingC1 = null;
						pendingC2 = null;

						contourStart = shape.Nodes.Count;
						contourOpen = true;

						shape.Nodes.Add(new CurveNode
						{
							Position = p,
							Type = CurveNodeType.Straight,
							NewContour = true
						});

						break;
					}

				case PdfNative.FPDF_SEGMENT_LINETO:
					{
						if (!contourOpen)
							break;

						shape.Nodes.Add(new CurveNode
						{
							Position = p,
							Type = CurveNodeType.Straight
						});

						break;
					}

				case PdfNative.FPDF_SEGMENT_BEZIERTO:
					{
						if (!contourOpen)
							break;

						if (pendingC1 == null)
						{
							pendingC1 = p;
							break;
						}

						if (pendingC2 == null)
						{
							pendingC2 = p;
							break;
						}

						// Третья точка — конец сегмента. Ручки навешиваются
						// на соседние узлы: у VDraw они хранятся в узлах,
						// а не в сегменте.
						var from = shape.Nodes[^1];

						from.ControlNext = pendingC1.Value;
						PromoteToSharp(from);

						var to = new CurveNode
						{
							Position = p,
							Type = CurveNodeType.Sharp,
							ControlPrev = pendingC2.Value
						};

						shape.Nodes.Add(to);

						pendingC1 = null;
						pendingC2 = null;

						break;
					}
			}

			if (closes && contourOpen && contourStart >= 0 && shape.Nodes.Count > 0)
			{
				shape.Nodes[^1].CloseAfter = true;
				contourOpen = false;
			}
		}

		if (shape.Nodes.Count < 2)
			return null;

		ApplyPaint(ctx, shape, obj, full, filled, stroked);

		if (ctx.Options.SkipInvisible && IsInvisible(shape))
			return null;

		Recenter(shape);

		return shape;
	}

	/// <summary>
	/// Узел с ручками обязан иметь тип, отличный от Straight: свойства
	/// HasPrevControl и HasNextControl проверяют именно его, и на
	/// Straight ручки просто не читаются.
	/// </summary>
	private static void PromoteToSharp(CurveNode node)
	{
		if (node.Type == CurveNodeType.Straight)
			node.Type = CurveNodeType.Sharp;
	}

	#endregion

	#region Оформление

	private static void ApplyPaint(
		BuildContext ctx, Shape shape, IntPtr obj, in SKMatrix full, bool filled, bool stroked)
	{
		if (filled && PdfNative.FPDFPageObj_GetFillColor(obj, out uint fr, out uint fg, out uint fb, out uint fa))
		{
			shape.Fill.Enabled = fa > 0;
			shape.Fill.Kind = FillKind.Solid;
			shape.Fill.SolidColor = new SKColor((byte)fr, (byte)fg, (byte)fb, (byte)fa);
		}
		else
		{
			shape.Fill.Enabled = false;
		}

		if (!stroked)
		{
			shape.Stroke.Enabled = false;
			return;
		}

		if (PdfNative.FPDFPageObj_GetStrokeColor(obj, out uint sr, out uint sg, out uint sb, out uint sa))
		{
			shape.Stroke.Enabled = sa > 0;
			shape.Stroke.Color = new SKColor((byte)sr, (byte)sg, (byte)sb, (byte)sa);
		}
		else
		{
			shape.Stroke.Enabled = true;
		}

		// Толщина задана в пользовательском пространстве объекта, поэтому
		// масштабируется вместе с ним. Изотропный множитель берётся как
		// корень из определителя: для неравномерного масштаба это
		// компромисс, но иного представления у одной толщины нет.
		float scale = ScaleOf(full);
		float mmPerUom = MathF.Max(ctx.TargetPage.MmPerUom, 0.0001f);

		// Толщина хранится в единицах страницы, поэтому перевод в миллиметры
		// здесь больше не нужен: widthUser * scale уже даёт единицы страницы.
		// Волосяная линия задана физической величиной и переводится обратно.
		float hairlineUom = HairlineMm / mmPerUom;
		float widthUom = hairlineUom;

		if (PdfNative.FPDFPageObj_GetStrokeWidth(obj, out float widthUser) && widthUser > 0f)
			widthUom = MathF.Max(widthUser * scale, hairlineUom);

		shape.Stroke.Thickness = widthUom;

		ApplyDash(shape, obj, scale);
	}

	/// <summary>
	/// Массив штрихов PDF ложится на StrokeStyle без потерь: Pattern
	/// хранит относительные доли, PatternLength — длину полного цикла.
	/// Записываем доли прямо в единицах страницы, тогда цикл равен их сумме
	/// и масштабирование внутри CreatePaint выходит единичным.
	/// </summary>
	private static void ApplyDash(Shape shape, IntPtr obj, float uomPerUserUnit)
	{
		float[] dashes = PdfNative.GetDashArray(obj);

		if (dashes.Length == 0)
		{
			shape.Stroke.Pattern = string.Empty;
			return;
		}

		var values = new List<float>(dashes.Length + 1);
		float total = 0f;

		foreach (float d in dashes)
		{
			float uom = MathF.Max(d * uomPerUserUnit, 0f);

			values.Add(uom);
			total += uom;
		}

		// Нечётная длина массива в PDF означает повтор со сменой фазы;
		// SKPathEffect требует чётного числа интервалов.
		if ((values.Count % 2) != 0)
		{
			values.AddRange(values.ToArray());
			total *= 2f;
		}

		if (total <= 0.0001f)
		{
			shape.Stroke.Pattern = string.Empty;
			return;
		}

		shape.Stroke.Pattern = string.Join(
			" ", values.Select(v => v.ToString("0.###", CultureInfo.InvariantCulture)));

		shape.Stroke.PatternLength = total;
	}

	private static bool IsInvisible(Shape shape)
	{
		bool noFill = !shape.Fill.Enabled || shape.Fill.SolidColor.Alpha == 0;
		bool noStroke = !shape.Stroke.Enabled || shape.Stroke.Color.Alpha == 0;

		return noFill && noStroke;
	}

	#endregion

	#region Текст

	private static Shape? BuildText(BuildContext ctx, IntPtr obj, in SKMatrix full)
	{
		if (!ctx.Options.TextAsText || ctx.TextPage == IntPtr.Zero)
			return null;

		// Режим 3 — невидимый текст: слой OCR под сканом. Импортировать
		// его поверх картинки значит вывалить на лист дубль всего текста.
		int renderMode = PdfNative.FPDFTextObj_GetTextRenderMode(obj);

		if (renderMode == 3 && ctx.Options.SkipInvisible)
			return null;

		string text = PdfNative.GetObjectText(obj, ctx.TextPage);

		if (string.IsNullOrWhiteSpace(text))
			return null;

		if (!PdfNative.FPDFTextObj_GetFontSize(obj, out float fontSize) || fontSize <= 0f)
			fontSize = 12f;

		// Направление базовой линии после всех преобразований, включая
		// переворот оси Y. Зеркалить ничего не нужно: переворот уже
		// заложен в матрице страницы.
		var origin = full.MapPoint(0f, 0f);
		var xTip = full.MapPoint(1f, 0f);
		var yTip = full.MapPoint(0f, 1f);

		float dx = xTip.X - origin.X;
		float dy = xTip.Y - origin.Y;

		float vy = MathF.Sqrt(
			(yTip.X - origin.X) * (yTip.X - origin.X) +
			(yTip.Y - origin.Y) * (yTip.Y - origin.Y));

		float sizeUom = fontSize * vy;
		float mmPerUom = MathF.Max(ctx.TargetPage.MmPerUom, 0.0001f);

		// Разбор шрифта вынесен в резолвер: он же извлекает встроенную
		// программу шрифта, регистрирует её и накапливает отчёт
		// об отсутствующих гарнитурах.
		var fontUsage = ctx.Fonts.Resolve(PdfNative.FPDFTextObj_GetFont(obj));

		string family = fontUsage.ResolvedFamily;
		bool bold = fontUsage.Bold;
		bool italic = fontUsage.Italic;

		var shape = new TextShape
		{
			Text = text,
			LocalOrigin = SKPoint.Empty,
			FontSizeMm = MathF.Max(sizeUom * mmPerUom, 0.1f),
			FontFamily = family,
			Bold = bold,
			Italic = italic,
			Name = "PDF text"
		};

		if (PdfNative.FPDFPageObj_GetFillColor(obj, out uint r, out uint g, out uint b, out uint a))
		{
			shape.Fill.Enabled = a > 0;
			shape.Fill.Kind = FillKind.Solid;
			shape.Fill.SolidColor = new SKColor((byte)r, (byte)g, (byte)b, (byte)a);
		}

		var transform = SKMatrix.CreateTranslation(origin.X, origin.Y);

		float angle = MathF.Atan2(dy, dx) * 180f / MathF.PI;

		if (MathF.Abs(angle) > 0.01f)
			transform = Multiply(transform, SKMatrix.CreateRotationDegrees(angle));

		shape.LocalTransform = transform;

		return shape;
	}

	#endregion

	#region Растровые объекты

	private static Shape? BuildImage(BuildContext ctx, IntPtr obj, in SKMatrix full)
	{
		IntPtr fpdfBitmap = PdfNative.FPDFImageObj_GetRenderedBitmap(ctx.Doc.Handle, ctx.Page, obj);

		if (fpdfBitmap == IntPtr.Zero)
			return null;

		SKBitmap? bitmap = null;

		try
		{
			bitmap = CopyBitmap(fpdfBitmap);
		}
		finally
		{
			PdfNative.FPDFBitmap_Destroy(fpdfBitmap);
		}

		if (bitmap == null)
			return null;

		// Картинка в PDF занимает единичный квадрат, ось Y направлена
		// вверх: верхний левый угол изображения — это точка (0, 1),
		// а не (0, 0).
		var topLeft = full.MapPoint(0f, 1f);
		var topRight = full.MapPoint(1f, 1f);
		var bottomLeft = full.MapPoint(0f, 0f);

		float width = Distance(topLeft, topRight);
		float height = Distance(topLeft, bottomLeft);

		if (width <= 0.0001f || height <= 0.0001f)
		{
			bitmap.Dispose();
			return null;
		}

		var rect = new SKRect(-width * 0.5f, -height * 0.5f, width * 0.5f, height * 0.5f);

		var center = new SKPoint(
			topLeft.X + ((topRight.X - topLeft.X) + (bottomLeft.X - topLeft.X)) * 0.5f,
			topLeft.Y + ((topRight.Y - topLeft.Y) + (bottomLeft.Y - topLeft.Y)) * 0.5f);

		var transform = SKMatrix.CreateTranslation(center.X, center.Y);

		float angle = MathF.Atan2(topRight.Y - topLeft.Y, topRight.X - topLeft.X) * 180f / MathF.PI;

		if (MathF.Abs(angle) > 0.01f)
			transform = Multiply(transform, SKMatrix.CreateRotationDegrees(angle));

		var shape = new ImageShape
		{
			Name = "PDF image",
			ImageLocalRect = rect,
			LocalTransform = transform
		};

		AddRectNodes(shape, rect);

		shape.Fill.Enabled = false;
		shape.Stroke.Enabled = false;

		shape.SetOriginalBitmap(bitmap);
		bitmap.Dispose();

		return shape;
	}

	private static SKBitmap? CopyBitmap(IntPtr fpdfBitmap)
	{
		int width = PdfNative.FPDFBitmap_GetWidth(fpdfBitmap);
		int height = PdfNative.FPDFBitmap_GetHeight(fpdfBitmap);
		int stride = PdfNative.FPDFBitmap_GetStride(fpdfBitmap);
		int format = PdfNative.FPDFBitmap_GetFormat(fpdfBitmap);

		IntPtr source = PdfNative.FPDFBitmap_GetBuffer(fpdfBitmap);

		if (width <= 0 || height <= 0 || source == IntPtr.Zero)
			return null;

		var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
		var bitmap = new SKBitmap(info);

		IntPtr dest = bitmap.GetPixels();

		if (dest == IntPtr.Zero)
		{
			bitmap.Dispose();
			return null;
		}

		int destStride = bitmap.RowBytes;

		unsafe
		{
			byte* src = (byte*)source;
			byte* dst = (byte*)dest;

			for (int y = 0; y < height; y++)
			{
				byte* srcRow = src + (long)y * stride;
				byte* dstRow = dst + (long)y * destStride;

				switch (format)
				{
					case PdfNative.FPDFBitmap_BGRA:
						Buffer.MemoryCopy(srcRow, dstRow, destStride, Math.Min(stride, destStride));
						break;

					case PdfNative.FPDFBitmap_BGRx:
						for (int x = 0; x < width; x++)
						{
							dstRow[x * 4 + 0] = srcRow[x * 4 + 0];
							dstRow[x * 4 + 1] = srcRow[x * 4 + 1];
							dstRow[x * 4 + 2] = srcRow[x * 4 + 2];
							dstRow[x * 4 + 3] = 255;
						}
						break;

					case PdfNative.FPDFBitmap_BGR:
						for (int x = 0; x < width; x++)
						{
							dstRow[x * 4 + 0] = srcRow[x * 3 + 0];
							dstRow[x * 4 + 1] = srcRow[x * 3 + 1];
							dstRow[x * 4 + 2] = srcRow[x * 3 + 2];
							dstRow[x * 4 + 3] = 255;
						}
						break;

					case PdfNative.FPDFBitmap_Gray:
						for (int x = 0; x < width; x++)
						{
							byte v = srcRow[x];

							dstRow[x * 4 + 0] = v;
							dstRow[x * 4 + 1] = v;
							dstRow[x * 4 + 2] = v;
							dstRow[x * 4 + 3] = 255;
						}
						break;

					default:
						bitmap.Dispose();
						return null;
				}
			}
		}

		bitmap.NotifyPixelsChanged();

		return bitmap;
	}

	#endregion

	#region Вспомогательное

	internal static void AddRectNodes(CurveShape shape, SKRect r)
	{
		var corners = new[]
		{
			new SKPoint(r.Left, r.Top),
			new SKPoint(r.Right, r.Top),
			new SKPoint(r.Right, r.Bottom),
			new SKPoint(r.Left, r.Bottom)
		};

		for (int i = 0; i < 4; i++)
		{
			shape.Nodes.Add(new CurveNode
			{
				Position = corners[i],
				Type = CurveNodeType.Straight,
				NewContour = i == 0,
				CloseAfter = i == 3
			});
		}
	}

	/// <summary>
	/// Переносит узлы в систему координат вокруг собственного центра,
	/// а смещение убирает в LocalTransform. Так же поступает импорт
	/// Office: манипуляторы поворота и масштаба рассчитывают, что
	/// начало локальной системы совпадает с центром фигуры.
	/// </summary>
	private static void Recenter(CurveShape shape)
	{
		if (shape.Nodes.Count == 0)
			return;

		var bounds = shape.GetLocalBounds();

		float cx = bounds.MidX;
		float cy = bounds.MidY;

		if (MathF.Abs(cx) < 0.0001f && MathF.Abs(cy) < 0.0001f)
			return;

		foreach (var node in shape.Nodes)
		{
			node.Position = new SKPoint(node.Position.X - cx, node.Position.Y - cy);

			if (node.HasPrevControl)
				node.ControlPrev = new SKPoint(node.ControlPrev.X - cx, node.ControlPrev.Y - cy);

			if (node.HasNextControl)
				node.ControlNext = new SKPoint(node.ControlNext.X - cx, node.ControlNext.Y - cy);
		}

		shape.LocalTransform = SKMatrix.CreateTranslation(cx, cy);
	}

	/// <summary>Матрица из коэффициентов PDF [a b c d e f].</summary>
	private static SKMatrix FromPdf(float a, float b, float c, float d, float e, float f)
	{
		return new SKMatrix
		{
			ScaleX = a,
			SkewY = b,
			SkewX = c,
			ScaleY = d,
			TransX = e,
			TransY = f,
			Persp0 = 0f,
			Persp1 = 0f,
			Persp2 = 1f
		};
	}

	/// <summary>Результат применяет сначала <paramref name="before"/>.</summary>
	private static SKMatrix Multiply(in SKMatrix after, in SKMatrix before)
	{
		return new SKMatrix
		{
			ScaleX = after.ScaleX * before.ScaleX + after.SkewX * before.SkewY,
			SkewX = after.ScaleX * before.SkewX + after.SkewX * before.ScaleY,
			TransX = after.ScaleX * before.TransX + after.SkewX * before.TransY + after.TransX,

			SkewY = after.SkewY * before.ScaleX + after.ScaleY * before.SkewY,
			ScaleY = after.SkewY * before.SkewX + after.ScaleY * before.ScaleY,
			TransY = after.SkewY * before.TransX + after.ScaleY * before.TransY + after.TransY,

			Persp0 = 0f,
			Persp1 = 0f,
			Persp2 = 1f
		};
	}

	private static float ScaleOf(in SKMatrix m)
	{
		float det = MathF.Abs(m.ScaleX * m.ScaleY - m.SkewX * m.SkewY);

		return det > 0f ? MathF.Sqrt(det) : 0f;
	}

	private static float Distance(SKPoint a, SKPoint b)
	{
		float dx = b.X - a.X;
		float dy = b.Y - a.Y;

		return MathF.Sqrt(dx * dx + dy * dy);
	}

	#endregion
}
