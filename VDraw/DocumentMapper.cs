using SkiaSharp;

using VDraw.Animation;
using VDraw.Editing;
using VDraw.Editing.Images;
using VDraw.History;
using VDraw.Shapes;
using VDraw.Ui;

namespace VDraw.Documents;

/// <summary>
/// Перевод документа в модель файла и обратно.
///
/// Отдельный слой, а не атрибуты на самих классах фигур. Причины три:
/// у фигур есть поля, которых в файле быть не должно (кэши путей, превью
/// мазка, закэшированный SKPaint коррекций); привязки и текст ссылаются
/// на другие фигуры и образуют циклы, которые JSON не выражает; и наконец
/// формат файла хочется менять независимо от внутренней модели.
///
/// Чтение идёт в два прохода. Первый создаёт все фигуры и складывает их
/// в таблицу по идентификатору, второй расставляет ссылки: привязки концов,
/// фигуру-контур для текста вдоль пути и фигуру-контейнер для текста внутри.
/// Одним проходом это не сделать — цель может стоять в файле позже ссылки.
/// </summary>
public static class DocumentMapper
{
	#region Виды фигур

	private const string KindRectangle = "Rectangle";
	private const string KindRoundedRectangle = "RoundedRectangle";
	private const string KindChamferedRectangle = "ChamferedRectangle";
	private const string KindCircle2P = "Circle2P";
	private const string KindCircle3P = "Circle3P";
	private const string KindEllipse3P = "Ellipse3P";
	private const string KindEllipse5P = "Ellipse5P";
	private const string KindPolygon = "Polygon";
	private const string KindCurve = "Curve";
	private const string KindImage = "Image";
	private const string KindText = "Text";
	private const string KindMeasurement = "Measurement";
	private const string KindAngleMeasurement = "AngleMeasurement";
	private const string KindGuide = "Guide";
	private const string KindGroup = "Group";
	private const string KindTransform = "Transform";

	#endregion

	#region Документ

	/// <summary>
	/// Собирает содержимое файла из документа.
	///
	/// Версия проставляется ТЕКУЩАЯ, независимо от того, из файла какой
	/// версии документ прочитан. Запись идёт нынешним кодом и нынешними
	/// правилами, поэтому и помечена должна быть нынешним номером:
	/// сохранённый старый файл становится новым, и читать его дальше
	/// надо по новым правилам.
	/// </summary>
	public static DocumentFileDto ToDto(Document doc)
	{
		var dto = new DocumentFileDto
		{
			Version = DocumentFileDto.CurrentVersion,
			AppVersion = AppInfo.Version,
			AppBuild = AppInfo.Build,
			Title = doc.title,
			FileName = doc.FileName,
			PaperFormat = ToDto(doc.paper_format),
			Uom = ToDto(doc.uom),
			PaperSizeUom = doc.size_uom,
			DPI = doc.DPI,
			DefaultPageOrientation = doc.Orientation,
			ActivePageIndex = doc.ActivePage != null ? Math.Max(0, doc.pages.IndexOf(doc.ActivePage)) : 0
		};

		foreach (var page in doc.pages)
			dto.Pages.Add(ToDto(page));

		foreach (var custom in doc.CustomShapes)
			dto.CustomShapes.Add(custom.ToDto());

		return dto;
	}

	/// <summary>
	/// Наполняет документ содержимым файла. Документ должен быть пустым:
	/// страницы, созданные конструктором, вызывающий убирает сам, вместе
	/// с отпиской от истории.
	/// </summary>
	public static void Populate(Document doc, DocumentFileDto dto)
	{
		// Версия файла запоминается на время чтения: разбор фигур идёт
		// глубоко внутри, протаскивать номер параметром через каждый
		// уровень пришлось бы сквозь полтора десятка методов, которым
		// он не нужен.
		//
		// Поле статическое, и чтение двух документов одновременно оно
		// не переживёт. Это допустимо: загрузка идёт из обработчика
		// событий окна, то есть строго в одном потоке и по одному файлу.
		_loadingVersion = dto.Version;

		try
		{
			PopulateCore(doc, dto);
		}
		finally
		{
			_loadingVersion = DocumentFileDto.CurrentVersion;
		}
	}

	/// <summary>
	/// Версия файла, который читается прямо сейчас.
	///
	/// Вне чтения равна текущей: фигуры, созданные в работе, устроены
	/// по нынешним правилам, и любая проверка вида «версия меньше такой-то»
	/// для них обязана быть ложной.
	/// </summary>
	private static int _loadingVersion = DocumentFileDto.CurrentVersion;

	private static void PopulateCore(Document doc, DocumentFileDto dto)
	{
		doc.title = string.IsNullOrWhiteSpace(dto.Title) ? "Untitled" : dto.Title;
		doc.FileName = string.IsNullOrWhiteSpace(dto.FileName) ? "Untitled.vdr" : dto.FileName;
		doc.paper_format = FromDto(dto.PaperFormat) ?? new PaperFormat("A4", 210f, 297f);
		doc.uom = FromDto(dto.Uom) ?? new UoM("mm", 1f);
		doc.size_uom = dto.PaperSizeUom;
		doc.DPI = dto.DPI;
		doc.Orientation = dto.DefaultPageOrientation;

		doc.CustomShapes.Clear();

		foreach (var customDto in dto.CustomShapes)
		{
			doc.CustomShapes.Add(
				CustomShapeDefinition.FromDto(customDto, CustomShapeOrigin.Document));
		}

		foreach (var pageDto in dto.Pages)
			doc.AddPage(FromDto(pageDto, doc));

		if (doc.pages.Count == 0)
			doc.CreateDefaultPage();

		int index = Math.Clamp(dto.ActivePageIndex, 0, doc.pages.Count - 1);
		doc.ActivePage = doc.pages[index];

		SyncLayerCounter(doc);
	}

	/// <summary>
	/// Сдвигает общий счётчик слоёв за максимум загруженных.
	///
	/// Shape.Layer выдаётся из статического Settings.layer_start++. Без сдвига
	/// первые же новые фигуры получили бы номера, уже занятые загруженными,
	/// и порядок перекрытия оказался бы произвольным.
	/// </summary>
	private static void SyncLayerCounter(Document doc)
	{
		int max = Settings.layer_start;

		foreach (var page in doc.pages)
		{
			foreach (var shape in EnumerateDeep(page.Shapes))
			{
				if (shape.Layer == Shape.UnassignedLayer || shape.Layer == Shape.PreviewLayer)
					continue;

				max = Math.Max(max, shape.Layer + 1);
			}
		}

		Settings.layer_start = max;
	}

	private static IEnumerable<Shape> EnumerateDeep(IEnumerable<Shape> shapes)
	{
		foreach (var shape in shapes)
		{
			yield return shape;

			if (shape is GroupShape group)
			{
				foreach (var child in EnumerateDeep(group.Children))
					yield return child;
			}
		}
	}

	#endregion

	#region Страница

	public static PageDto ToDto(Page page)
	{
		var dto = new PageDto
		{
			Title = page.title,
			PaperFormat = ToDto(page.paper_format),
			Uom = ToDto(page.uom),
			SizeUom = page.size_uom,
			DPI = page.DPI,
			Orientation = page.Orientation,
			BackgroundColor = page.background_color,
			PageBgColor = page.page_bg_color,
			LockRatio = page.LockRatio,
			GridEnabled = page.GridEnabled,
			PrintBordersEnabled = page.PrintBordersEnabled,
			PageOrigin = page.page_origin,
			LockedRatio = page.locked_ratio
		};

		foreach (var shape in page.Shapes)
			dto.Shapes.Add(ToDto(shape));

		dto.Animation = ToDto(page.Animation);

		return dto;
	}

	public static Page FromDto(PageDto dto, Document doc)
	{
		var format = FromDto(dto.PaperFormat) ?? doc.paper_format ?? new PaperFormat("A4", 210f, 297f);
		var uom = FromDto(dto.Uom) ?? doc.uom ?? new UoM("mm", 1f);

		var page = new Page(format, uom, dto.DPI, dto.SizeUom.Width, dto.SizeUom.Height, dto.Orientation, doc)
		{
			title = dto.Title,
			size_uom = dto.SizeUom,
			background_color = dto.BackgroundColor,
			page_bg_color = dto.PageBgColor,
			LockRatio = dto.LockRatio,
			GridEnabled = dto.GridEnabled,
			PrintBordersEnabled = dto.PrintBordersEnabled,
			page_origin = dto.PageOrigin,
			locked_ratio = dto.LockedRatio
		};

		// Список, а не одна фигура на ключ: в файлах, записанных прежними
		// версиями, ключи повторяются. Копирование фигуры не выдавало копии
		// собственный идентификатор, и после Ctrl+C — Ctrl+V на странице
		// оказывались две фигуры с одним ключом. Таблица «ключ -> фигура»
		// оставляла из них одну, и все привязки, записанные на этот ключ,
		// сходились на ней — размеры с разных копий детали собирались
		// на одной. Разобрать такой файл можно только по геометрии,
		// поэтому кандидаты сохраняются все.
		var byId = new Dictionary<string, List<Shape>>(StringComparer.Ordinal);
		var pending = new List<(Shape Shape, ShapeDto Dto)>();

		// Первый проход: создаём фигуры без ссылок.
		foreach (var shapeDto in dto.Shapes)
		{
			var shape = FromDto(shapeDto, byId, pending, page.MmPerUom);

			if (shape != null)
				page.Shapes.Add(shape);
		}

		// Второй проход: ссылки между фигурами.
		foreach (var (shape, shapeDto) in pending)
			ResolveReferences(shape, shapeDto, byId);

		// Третий проход: развести повторяющиеся ключи. Ссылки к этому
		// моменту уже разрешены в объекты, поэтому смена ключа их не рвёт,
		// а следующее сохранение уйдёт в файл уже без двусмысленности.
		MakeIdsUnique(page.Shapes);

		ApplyDto(page.Animation, dto.Animation);

		page.SortLayers();
		return page;
	}

	#endregion

	#region Анимация

	public static PageAnimationDto ToDto(PageAnimation anim)
	{
		var dto = new PageAnimationDto
		{
			FrameCount = anim.FrameCount,
			CurrentFrame = anim.CurrentFrame,
			Fps = anim.Fps
		};

		foreach (var shape in anim.Shapes.Values)
		{
			if (!shape.HasAnyKeys)
				continue;

			var shapeDto = new ShapeAnimationDto { ShapeId = shape.ShapeId.ToString() };

			foreach (var track in shape.Tracks.Values)
			{
				if (track.Keys.Count == 0)
					continue;

				var trackDto = new AnimTrackDto
				{
					ParamId = track.ParamId,
					Title = track.Title
				};

				foreach (var key in track.Keys)
					trackDto.Keys.Add($"{key.Frame}={key.Value.Serialize()}");

				shapeDto.Tracks.Add(trackDto);
			}

			if (shapeDto.Tracks.Count > 0)
				dto.Shapes.Add(shapeDto);
		}

		return dto;
	}

	/// <summary>
	/// Заполняет анимацию страницы из файла. Не создаёт объект, а наполняет
	/// существующий: PageAnimation принадлежит странице и заводится вместе
	/// с ней.
	///
	/// Испорченная строка ключа пропускается молча — как и укороченные
	/// координаты в остальном формате: документ должен открыться
	/// с потерей одного значения, а не исключением.
	/// </summary>
	public static void ApplyDto(PageAnimation anim, PageAnimationDto? dto)
	{
		anim.Clear();

		if (dto == null)
			return;

		anim.FrameCount = dto.FrameCount;
		anim.Fps = dto.Fps;
		anim.CurrentFrame = dto.CurrentFrame;

		foreach (var shapeDto in dto.Shapes)
		{
			if (!Guid.TryParse(shapeDto.ShapeId, out var shapeId))
				continue;

			var shapeAnim = anim.GetOrCreate(shapeId);

			foreach (var trackDto in shapeDto.Tracks)
			{
				if (string.IsNullOrEmpty(trackDto.ParamId))
					continue;

				var track = shapeAnim.GetOrCreate(trackDto.ParamId, trackDto.Title);

				foreach (var raw in trackDto.Keys)
				{
					int sep = raw.IndexOf('=');

					if (sep <= 0)
						continue;

					if (!int.TryParse(raw.AsSpan(0, sep), out int frame))
						continue;

					if (!AnimValue.TryParse(raw[(sep + 1)..], out var value))
						continue;

					track.Set(frame, value);
				}
			}
		}

		// Ключи за пределами длины анимации подтягиваются к последнему
		// кадру: файл мог быть записан версией с другим пределом.
		anim.ClampKeysToLength();
	}

	#endregion

	#region Фигуры: запись

	public static ShapeDto ToDto(Shape shape)
	{
		var dto = new ShapeDto
		{
			Kind = KindOf(shape),
			Id = shape.Id.ToString("N"),
			Name = shape.Name,
			Layer = shape.Layer,
			IsVisible = shape.IsVisible,
			IsLocked = shape.IsLocked,
			LocalTransform = shape.LocalTransform,
			Stroke = ToDto(shape.Stroke),
			Fill = ToDto(shape.Fill),
			Shadow = ToDto(shape.Shadow),
			Transparency = ToDto(shape.Transparency),
			OtherShapeId = shape.otherShape?.Id.ToString("N")
		};

		switch (shape)
		{
			case RoundedRectangleShape rr:
				dto.Rect = rr.LocalRect;
				dto.RadiusX = rr.RadiusX;
				dto.RadiusY = rr.RadiusY;
				break;

			case ChamferedRectangleShape cr:
				dto.Rect = cr.LocalRect;
				dto.ChamferX = cr.ChamferX;
				dto.ChamferY = cr.ChamferY;
				break;

			case RectangleShape rect:
				dto.Rect = rect.LocalRect;
				break;

			case Circle2PShape c2:
				dto.LocalCenter = c2.LocalCenter;
				dto.Radius = c2.Radius;
				dto.CenterPoint = c2.CenterPoint;
				dto.RadiusPoint = c2.RadiusPoint;
				break;

			case Circle3PShape c3:
				dto.P1 = c3.P1;
				dto.P2 = c3.P2;
				dto.P3 = c3.P3;
				break;

			case Ellipse3PShape e3:
				dto.LocalCenter = e3.LocalCenter;
				dto.RadiusX = e3.RadiusX;
				dto.RadiusY = e3.RadiusY;
				dto.CenterPoint = e3.CenterPoint;
				dto.RadiusPoint1 = e3.RadiusPoint1;
				dto.RadiusPoint2 = e3.RadiusPoint2;
				break;

			case Ellipse5PShape e5:
				dto.LocalCenter = e5.LocalCenter;
				dto.RadiusX = e5.RadiusX;
				dto.RadiusY = e5.RadiusY;
				dto.Points = new List<SKPoint>(e5.Points);
				break;

			case PolygonShape poly:
				dto.LocalCenter = poly.LocalCenter;
				dto.RadiusX = poly.RadiusX;
				dto.RadiusY = poly.RadiusY;
				dto.RotationRad = poly.RotationRad;
				dto.Sides = poly.Sides;
				dto.UseExplicitPoints = poly.UseExplicitPoints;
				dto.Points = new List<SKPoint>(poly.Points);
				break;

			case AngleMeasurementShape angleMeasurement:
				dto.AngleMeasurement = ToDto(angleMeasurement);
				break;

			case MeasurementShape measurement:
				dto.Measurement = ToDto(measurement);
				dto.FirstAnchor = ToDto(measurement.FirstAnchor);
				dto.LastAnchor = ToDto(measurement.LastAnchor);
				break;

			case GuideLine guide:
				dto.Guide = new GuideDto
				{
					Orientation = guide.Orientation,
					TransformMode = guide.TransformMode,
					GuidePoint = guide.GuidePoint,
					AngleDeg = guide.AngleDeg
				};
				break;

			case GroupShape group:
				dto.Children = group.Children.Select(ToDto).ToList();
				break;

			case TextShape text:
				dto.Text = ToDto(text);
				break;
		}

		// Кривая и её наследники: узлы пишутся всегда, изображение добавляет
		// к ним пиксели. Проверка отдельно от switch выше, потому что
		// ImageShape наследуется от CurveShape и должен получить и то, и другое.
		if (shape is CurveShape curve)
		{
			dto.Nodes = curve.Nodes.Select(ToDto).ToList();
			dto.FillType = curve.FillType;
			dto.AnchorStubFactor = curve.AnchorStubFactor;
			dto.FirstAnchor = ToDto(curve.FirstAnchor);
			dto.LastAnchor = ToDto(curve.LastAnchor);
		}

		if (shape is ImageShape image)
			dto.Image = ToDto(image);

		if (shape is TransformShape transform)
		{
			dto.Transform = new TransformToolDto
			{
				// Из ключей, а не из ссылок: ключ есть и тогда, когда ссылку
				// ещё не разрешили, и переживает подмену экземпляра.
				ShapeAId = transform.ShapeAId != Guid.Empty ? transform.ShapeAId.ToString("N") : null,
				ShapeBId = transform.ShapeBId != Guid.Empty ? transform.ShapeBId.ToString("N") : null,
				Steps = transform.Steps,
				Bias = transform.Bias,
				P1 = transform.P1,
				P2 = transform.P2,
				PointsInitialized = transform.PointsInitialized
			};
		}

		return dto;
	}

	private static string KindOf(Shape shape) => shape switch
	{
		TransformShape => KindTransform,
		ImageShape => KindImage,
		AngleMeasurementShape => KindAngleMeasurement,
		MeasurementShape => KindMeasurement,
		GuideLine => KindGuide,
		GroupShape => KindGroup,
		TextShape => KindText,
		RoundedRectangleShape => KindRoundedRectangle,
		ChamferedRectangleShape => KindChamferedRectangle,
		RectangleShape => KindRectangle,
		Circle2PShape => KindCircle2P,
		Circle3PShape => KindCircle3P,
		Ellipse3PShape => KindEllipse3P,
		Ellipse5PShape => KindEllipse5P,
		PolygonShape => KindPolygon,
		CurveShape => KindCurve,
		_ => KindCurve
	};

	#endregion

	#region Эффекты

	/// <summary>
	/// Выключенный и ни разу не тронутый эффект в файл не пишется: такой
	/// объект есть у каждой фигуры, и узел для него раздувал бы документ,
	/// ничего не сообщая.
	/// </summary>
	private static ShadowDto? ToDto(ShadowEffect? shadow)
	{
		if (shadow == null || (!shadow.Enabled && !shadow.OffsetsInitialized))
			return null;

		return new ShadowDto
		{
			Enabled = shadow.Enabled,
			Color = shadow.Color,
			Softness = shadow.Softness,
			CenterOffset = shadow.CenterOffset,
			SizeVector = shadow.SizeVector,
			OffsetsInitialized = shadow.OffsetsInitialized
		};
	}

	private static ShadowEffect FromDto(ShadowDto dto)
	{
		return new ShadowEffect
		{
			Enabled = dto.Enabled,
			Color = dto.Color,
			Softness = dto.Softness,
			CenterOffset = dto.CenterOffset,
			SizeVector = dto.SizeVector,
			OffsetsInitialized = dto.OffsetsInitialized
		};
	}

	private static TransparencyDto? ToDto(TransparencyEffect? transparency)
	{
		if (transparency == null || (!transparency.Enabled && !transparency.OffsetsInitialized))
			return null;

		return new TransparencyDto
		{
			Enabled = transparency.Enabled,
			StartOpacity = transparency.StartOpacity,
			EndOpacity = transparency.EndOpacity,
			StartOffset = transparency.StartOffset,
			EndOffset = transparency.EndOffset,
			OffsetsInitialized = transparency.OffsetsInitialized
		};
	}

	private static TransparencyEffect FromDto(TransparencyDto dto)
	{
		return new TransparencyEffect
		{
			Enabled = dto.Enabled,
			StartOpacity = dto.StartOpacity,
			EndOpacity = dto.EndOpacity,
			StartOffset = dto.StartOffset,
			EndOffset = dto.EndOffset,
			OffsetsInitialized = dto.OffsetsInitialized
		};
	}

	#endregion

	#region Фигуры: чтение

	private static Shape? FromDto(
		ShapeDto dto,
		Dictionary<string, List<Shape>> byId,
		List<(Shape, ShapeDto)> pending,
		float mmPerUom)
	{
		Shape? shape = CreateShape(dto, byId, pending, mmPerUom);

		if (shape == null)
			return null;

		shape.Name = dto.Name;
		shape.Layer = dto.Layer;
		shape.IsVisible = dto.IsVisible;
		shape.IsLocked = dto.IsLocked;
		shape.LocalTransform = dto.LocalTransform;

		if (dto.Stroke != null)
			shape.Stroke = FromDto(dto.Stroke, mmPerUom);

		if (dto.Fill != null)
			shape.Fill = FromDto(dto.Fill);

		// Присваивание только при наличии узла: у фигуры уже есть
		// выключенный эффект из конструктора, и обнулять его нечем.
		if (dto.Shadow != null)
			shape.Shadow = FromDto(dto.Shadow);

		if (dto.Transparency != null)
			shape.Transparency = FromDto(dto.Transparency);

		// Идентификатор восстанавливается из файла, иначе ссылки, записанные
		// на него, никуда не приведут.
		if (Guid.TryParseExact(dto.Id, "N", out var id))
			shape.Id = id;

		if (!string.IsNullOrEmpty(dto.Id))
		{
			if (!byId.TryGetValue(dto.Id, out var sameId))
				byId[dto.Id] = sameId = new List<Shape>(1);

			sameId.Add(shape);
		}

		pending.Add((shape, dto));
		return shape;
	}

	private static Shape? CreateShape(
		ShapeDto dto,
		Dictionary<string, List<Shape>> byId,
		List<(Shape, ShapeDto)> pending,
		float mmPerUom)
	{
		switch (dto.Kind)
		{
			case KindRectangle:
				return new RectangleShape { LocalRect = dto.Rect ?? SKRect.Empty };

			case KindRoundedRectangle:
				return new RoundedRectangleShape
				{
					LocalRect = dto.Rect ?? SKRect.Empty,
					RadiusX = dto.RadiusX ?? 0f,
					RadiusY = dto.RadiusY ?? 0f
				};

			case KindChamferedRectangle:
				return new ChamferedRectangleShape
				{
					LocalRect = dto.Rect ?? SKRect.Empty,
					ChamferX = dto.ChamferX ?? 0f,
					ChamferY = dto.ChamferY ?? 0f
				};

			case KindCircle2P:
				return new Circle2PShape
				{
					LocalCenter = dto.LocalCenter ?? SKPoint.Empty,
					Radius = dto.Radius ?? 0f,
					CenterPoint = dto.CenterPoint ?? SKPoint.Empty,
					RadiusPoint = dto.RadiusPoint ?? SKPoint.Empty
				};

			case KindCircle3P:
				return new Circle3PShape
				{
					P1 = dto.P1 ?? SKPoint.Empty,
					P2 = dto.P2 ?? SKPoint.Empty,
					P3 = dto.P3 ?? SKPoint.Empty
				};

			case KindEllipse3P:
				return new Ellipse3PShape
				{
					LocalCenter = dto.LocalCenter ?? SKPoint.Empty,
					RadiusX = dto.RadiusX ?? 0f,
					RadiusY = dto.RadiusY ?? 0f,
					CenterPoint = dto.CenterPoint ?? SKPoint.Empty,
					RadiusPoint1 = dto.RadiusPoint1 ?? SKPoint.Empty,
					RadiusPoint2 = dto.RadiusPoint2 ?? SKPoint.Empty
				};

			case KindEllipse5P:
				{
					var e5 = new Ellipse5PShape
					{
						LocalCenter = dto.LocalCenter ?? SKPoint.Empty,
						RadiusX = dto.RadiusX ?? 0f,
						RadiusY = dto.RadiusY ?? 0f
					};

					if (dto.Points != null)
						e5.Points.AddRange(dto.Points);

					return e5;
				}

			case KindPolygon:
				{
					var poly = new PolygonShape
					{
						LocalCenter = dto.LocalCenter ?? SKPoint.Empty,
						RadiusX = dto.RadiusX ?? 10f,
						RadiusY = dto.RadiusY ?? 10f,
						RotationRad = dto.RotationRad ?? 0f,
						Sides = dto.Sides ?? 5,
						UseExplicitPoints = dto.UseExplicitPoints ?? false
					};

					if (dto.Points != null)
						poly.Points.AddRange(dto.Points);

					// Вершины взяты из файла как есть. RebuildPoints здесь
					// вызывать нельзя: у произвольного многоугольника он
					// ничего не делает, а у правильного пересчёт по округлённым
					// параметрам сдвинул бы вершины относительно записанных.
					poly.MarkGeometryInitialized();
					return poly;
				}

			case KindAngleMeasurement:
				return FromDto(dto.AngleMeasurement);

			case KindMeasurement:
				return FromDto(dto.Measurement);

			case KindGuide:
				{
					var g = dto.Guide;

					return new GuideLine
					{
						Orientation = g?.Orientation ?? GuideOrientation.Vertical,
						TransformMode = g?.TransformMode ?? GuideTransformMode.Move,
						GuidePoint = g?.GuidePoint ?? SKPoint.Empty,
						AngleDeg = g?.AngleDeg ?? 0f
					};
				}

			case KindTransform:
				{
					var t = dto.Transform;

					var transform = new TransformShape();

					if (t != null)
					{
						transform.Steps = t.Steps <= 0 ? 5 : t.Steps;
						transform.Bias = t.Bias;

						// Порядок важен: сеттеры P1/P2 сами взводят
						// PointsInitialized, поэтому флаг из файла ставится
						// после них, а не до.
						transform.P1 = t.P1;
						transform.P2 = t.P2;
						transform.PointsInitialized = t.PointsInitialized;
					}

					return transform;
				}

			case KindGroup:
				{
					var group = new GroupShape();

					if (dto.Children != null)
					{
						foreach (var childDto in dto.Children)
						{
							var child = FromDto(childDto, byId, pending, mmPerUom);

							if (child == null)
								continue;

							// Не AddChild: тот пересчитывает матрицу ребёнка
							// под родителя через ReparentPreserveWorld, а она
							// уже прочитана из файла в правильном виде.
							child.SetParent(group);
							group.Children.Add(child);
						}
					}

					return group;
				}

			case KindText:
				return FromDto(dto.Text);

			case KindImage:
				{
					var image = new ImageShape();
					ApplyCurve(image, dto);
					ApplyImage(image, dto.Image);
					return image;
				}

			case KindCurve:
			default:
				{
					var curve = new CurveShape();
					ApplyCurve(curve, dto);
					return curve;
				}
		}
	}

	/// <summary>Узлы и параметры кривой. Общее для CurveShape и ImageShape.</summary>
	private static void ApplyCurve(CurveShape curve, ShapeDto dto)
	{
		if (dto.Nodes != null)
		{
			foreach (var node in dto.Nodes)
				curve.Nodes.Add(FromDto(node));
		}

		if (dto.FillType.HasValue)
			curve.FillType = dto.FillType.Value;

		if (dto.AnchorStubFactor.HasValue)
			curve.AnchorStubFactor = dto.AnchorStubFactor.Value;
	}

	/// <summary>
	/// Расстановка ссылок между фигурами. Второй проход чтения: к этому
	/// моменту в таблице лежат все фигуры страницы, включая вложенные в группы.
	/// </summary>
	private static void ResolveReferences(Shape shape, ShapeDto dto, Dictionary<string, List<Shape>> byId)
	{
		shape.otherShape = Resolve(dto.OtherShapeId, byId);

		if (shape is IShapeAnchorHost host)
		{
			ApplyAnchor(host.FirstAnchor, dto.FirstAnchor, byId, AnchorHint(shape, 0));
			ApplyAnchor(host.LastAnchor, dto.LastAnchor, byId, AnchorHint(shape, 1));
			host.InvalidateAnchors();
		}

		// Измерение угла держит четыре привязки и под IShapeAnchorHost
		// не подходит: у того интерфейса ровно два конца.
		if (shape is AngleMeasurementShape angleHost && dto.AngleMeasurement?.Anchors != null)
		{
			var saved = dto.AngleMeasurement.Anchors;

			for (int i = 0; i < angleHost.Anchors.Length && i < saved.Length; i++)
				ApplyAnchor(angleHost.Anchors[i], saved[i], byId, AnchorHint(shape, i));

			angleHost.InvalidateAnchors();
		}

		if (shape is TransformShape transform && dto.Transform != null)
		{
			// Сначала ключи, затем ссылки: ключ должен уцелеть, даже если
			// фигуры в документе не оказалось и Resolve вернул null.
			if (Guid.TryParseExact(dto.Transform.ShapeAId ?? string.Empty, "N", out var aId))
				transform.ShapeAId = aId;

			if (Guid.TryParseExact(dto.Transform.ShapeBId ?? string.Empty, "N", out var bId))
				transform.ShapeBId = bId;

			transform.ShapeA = Resolve(dto.Transform.ShapeAId, byId);
			transform.ShapeB = Resolve(dto.Transform.ShapeBId, byId);

			// Ссылка не нашлась — файл правили вручную либо он повреждён.
			// Инструмент без пары фигур ничего не рисует и на странице
			// не мешает: пересчёт просто вернёт пустую форму.
			transform.InvalidateGeometry();
		}

		if (shape is TextShape text && dto.Text != null)
		{
			// Порядок важен: контейнер должен быть назначен до включения
			// режима, иначе сеттер InsideOtherShape увидит пустую ссылку.
			text.InsideShape = Resolve(dto.Text.InsideShapeId, byId);
			text.InsideOtherShape = dto.Text.InsideOtherShape;
			text.AlongShape = dto.Text.AlongShape;
			text.InvalidateAreaCache();
		}
	}

	private static void ApplyAnchor(
		ShapeAnchor anchor,
		AnchorDto? dto,
		Dictionary<string, List<Shape>> byId,
		SKPoint? hint)
	{
		if (dto == null)
			return;

		anchor.Enabled = dto.Enabled;
		anchor.TangentToEdge = dto.TangentToEdge;
		anchor.Kind = dto.Kind;
		anchor.Index = dto.Index;
		anchor.NodeId = Guid.TryParseExact(dto.NodeId, "N", out var nodeId) ? nodeId : Guid.Empty;
		anchor.LocalU = dto.LocalU;
		anchor.LocalV = dto.LocalV;

		// Файл старше четвёртой версии привязок к поверхности не знает.
		// Значение 5 в поле Kind там означало бы не Surface, а мусор:
		// перечисление тогда заканчивалось на Center. Сбрасываем такую
		// привязку, вместо того чтобы разрешать её по нулевым долям
		// и утаскивать конец размера в центр чужой фигуры.
		if (anchor.Kind == ShapeAnchorKind.Surface && _loadingVersion < 4)
		{
			anchor.Clear();
			anchor.Enabled = false;
			return;
		}

		anchor.Target = ResolveAnchorTarget(dto.TargetId, byId, anchor, hint);

		// Цель не нашлась — файл повреждён или фигуру удалили при правке
		// вручную. Оставлять вид привязки без цели нельзя: IsActive стал бы
		// false, а Kind продолжал бы вводить в заблуждение.
		if (anchor.Target == null)
			anchor.Kind = ShapeAnchorKind.None;
	}

	private static Shape? Resolve(string? id, Dictionary<string, List<Shape>> byId)
		=> !string.IsNullOrEmpty(id) && byId.TryGetValue(id, out var shapes) && shapes.Count > 0
			? shapes[0]
			: null;

	/// <summary>
	/// Цель привязки среди фигур с одинаковым ключом.
	///
	/// Пока ключ один — выбирать не из чего. Если ключей несколько,
	/// правильной считается фигура, точка привязки которой ближе всего
	/// к сохранённому положению конца. Это работает потому, что конец
	/// размера записан в файл в его собственных координатах и уже
	/// восстановлен: он показывает, ГДЕ этот конец стоял, когда файл
	/// сохраняли, а точка привязки каждой фигуры-кандидата вычисляется
	/// из её геометрии. Совпадение с точностью до долей единицы бывает
	/// только у той фигуры, к которой размер и был привязан.
	///
	/// Без подсказки берётся первый кандидат: это хотя бы устойчиво,
	/// в отличие от прежнего поведения, где выигрывал последний
	/// прочитанный и результат зависел от порядка фигур в файле.
	/// </summary>
	private static Shape? ResolveAnchorTarget(
		string? id,
		Dictionary<string, List<Shape>> byId,
		ShapeAnchor anchor,
		SKPoint? hint)
	{
		if (string.IsNullOrEmpty(id) || !byId.TryGetValue(id, out var candidates) || candidates.Count == 0)
			return null;

		if (candidates.Count == 1 || hint == null)
			return candidates[0];

		Shape? best = null;
		float bestSq = float.MaxValue;

		foreach (var candidate in candidates)
		{
			if (!TryGetAnchorWorldPoint(candidate, anchor, out var world))
				continue;

			float dx = world.X - hint.Value.X;
			float dy = world.Y - hint.Value.Y;
			float d2 = dx * dx + dy * dy;

			if (d2 >= bestSq)
				continue;

			bestSq = d2;
			best = candidate;
		}

		return best ?? candidates[0];
	}

	/// <summary>
	/// Мировая точка, которую даёт привязка на конкретной фигуре.
	/// </summary>
	private static bool TryGetAnchorWorldPoint(Shape target, ShapeAnchor anchor, out SKPoint world)
	{
		world = SKPoint.Empty;

		// У точки на поверхности перебирать нечего: доли задают её прямо.
		if (anchor.Kind == ShapeAnchorKind.Surface)
		{
			world = target.LocalToWorld(anchor.GetSurfaceLocal(target));
			return true;
		}

		// Временная привязка на кандидата: TryResolve умеет и узлы кривых
		// по идентификатору, и точки габаритов по номеру, и повторять
		// эту логику здесь было бы её вторым, расходящимся экземпляром.
		var probe = new ShapeAnchor
		{
			Enabled = true,
			Kind = anchor.Kind,
			Index = anchor.Index,
			NodeId = anchor.NodeId,
			TangentToEdge = anchor.TangentToEdge,
			Target = target
		};

		return ShapeAnchorGeometry.TryResolve(probe, out world, out _);
	}

	/// <summary>
	/// Где стоял конец размера в момент сохранения, в мировых координатах.
	///
	/// Только для измерений: у них положение концов записано в файл
	/// и уже прочитано. У кривой концы задаются узлами, а узел мог
	/// сдвинуться под привязку — как подсказка он менее надёжен, но
	/// и путаницы с копиями у связей flowchart не наблюдалось.
	/// </summary>
	private static SKPoint? AnchorHint(Shape shape, int index) => shape switch
	{
		MeasurementShape m => m.LocalToWorld(index == 0 ? m.P1 : m.P2),

		AngleMeasurementShape a => a.LocalToWorld(index switch
		{
			0 => a.S1P1,
			1 => a.S1P2,
			2 => a.S2P1,
			_ => a.S2P2
		}),

		_ => null
	};

	/// <summary>
	/// Выдаёт новые идентификаторы фигурам, у которых ключ совпал с уже
	/// встреченным.
	///
	/// Первая фигура с ключом его сохраняет, остальные получают свежие.
	/// Ссылки между фигурами к этому моменту разрешены в объекты, и смена
	/// ключа их не рвёт; исключение — инструмент пошагового преобразования,
	/// который хранит ссылки ключами, поэтому они переписываются из уже
	/// разрешённых объектов.
	/// </summary>
	private static void MakeIdsUnique(IEnumerable<Shape> shapes)
	{
		var seen = new HashSet<Guid>();

		foreach (var shape in EnumerateDeep(shapes))
		{
			if (!seen.Add(shape.Id))
				shape.Id = Guid.NewGuid();
		}

		foreach (var shape in EnumerateDeep(shapes))
		{
			if (shape is not TransformShape transform)
				continue;

			if (transform.ShapeA != null)
				transform.ShapeAId = transform.ShapeA.Id;

			if (transform.ShapeB != null)
				transform.ShapeBId = transform.ShapeB.Id;
		}
	}

	#endregion

	#region Стили

	private static StrokeDto ToDto(StrokeStyle s) => new()
	{
		Enabled = s.Enabled,
		Color = s.Color,
		Thickness = s.Thickness,
		Pattern = s.Pattern,
		PatternLength = s.PatternLength,
		StartGlyph = s.StartGlyph,
		EndGlyph = s.EndGlyph,
		StartGlyphSize = s.StartGlyphSize,
		EndGlyphSize = s.EndGlyphSize
	};

	/// <summary>
	/// Обводка из файла.
	///
	/// Признаком старого формата служит само наличие полей ThicknessMm
	/// и PatternLengthMm, а не номер версии: номер пришлось бы тащить
	/// через всю цепочку чтения ради значения, которое уже лежит в узле.
	/// Толщина в них записана в миллиметрах и переводится в единицы
	/// страницы, куда фигура попадёт.
	/// </summary>
	private static StrokeStyle FromDto(StrokeDto d, float mmPerUom) => new()
	{
		Enabled = d.Enabled,
		Color = d.Color,
		Thickness = d.ThicknessMm.HasValue
			? d.ThicknessMm.Value / MathF.Max(mmPerUom, 0.0001f)
			: d.Thickness,
		Pattern = d.Pattern,
		PatternLength = d.PatternLengthMm.HasValue
			? d.PatternLengthMm.Value / MathF.Max(mmPerUom, 0.0001f)
			: d.PatternLength,
		StartGlyph = d.StartGlyph,
		EndGlyph = d.EndGlyph,
		StartGlyphSize = d.StartGlyphSize,
		EndGlyphSize = d.EndGlyphSize
	};

	private static FillDto ToDto(FillStyle f) => new()
	{
		Kind = f.Kind,
		Enabled = f.Enabled,
		SolidColor = f.SolidColor,
		Tiling = f.Tiling,
		RotateWithShape = f.RotateWithShape,
		ScaleWithShape = f.ScaleWithShape,
		GradientColor1 = f.GradientColor1,
		GradientColor2 = f.GradientColor2,
		Stops = f.Stops.Select(s => new GradientStopDto { Offset = s.Offset, Color = s.Color }).ToList(),
		GradientMode = f.GradientMode,
		Angle = f.Angle,
		PatternFrontColor = f.PatternFrontColor,
		PatternBackColor = f.PatternBackColor,
		PatternWidth = f.PatternWidth,
		PatternHeight = f.PatternHeight,
		PatternId = f.PatternId,
		PatternDefinition = f.PatternDefinition,
		TextureId = f.TextureId,
		TextureFilePath = f.TextureFilePath,
		TexturePng = ImageCodec.Encode(f.TextureBitmap),
		StartPoint = f.StartPoint,
		EndPoint = f.EndPoint,
		FillTransformInitialized = f.FillTransformInitialized,
		SmoothTexture = f.SmoothTexture
	};

	private static FillStyle FromDto(FillDto d)
	{
		var fill = new FillStyle
		{
			Kind = d.Kind,
			Enabled = d.Enabled,
			SolidColor = d.SolidColor,
			Tiling = d.Tiling,
			RotateWithShape = d.RotateWithShape,
			ScaleWithShape = d.ScaleWithShape,
			GradientColor1 = d.GradientColor1,
			GradientColor2 = d.GradientColor2,
			GradientMode = d.GradientMode,
			Angle = d.Angle,
			PatternFrontColor = d.PatternFrontColor,
			PatternBackColor = d.PatternBackColor,
			PatternWidth = d.PatternWidth,
			PatternHeight = d.PatternHeight,
			PatternId = d.PatternId,
			PatternDefinition = d.PatternDefinition,
			TextureId = d.TextureId,
			TextureFilePath = d.TextureFilePath,
			StartPoint = d.StartPoint,
			EndPoint = d.EndPoint,
			FillTransformInitialized = d.FillTransformInitialized,
			SmoothTexture = d.SmoothTexture
		};

		// Список точек градиента создаётся конструктором с двумя записями
		// по умолчанию: без очистки к прочитанным добавились бы чужие.
		fill.Stops.Clear();

		foreach (var s in d.Stops)
			fill.Stops.Add(new GradientStop(s.Offset, s.Color));

		// Встроенная текстура имеет приоритет над путём: файл мог приехать
		// с другой машины, где библиотеки текстур нет.
		fill.TextureBitmap = ImageCodec.Decode(d.TexturePng);

		if (fill.TextureBitmap == null && !string.IsNullOrWhiteSpace(d.TextureId))
			fill.TextureBitmap = TextureLibrary.Find(d.TextureId)?.Bitmap;

		return fill;
	}

	#endregion

	#region Узлы и привязки

	private static CurveNodeDto ToDto(CurveNode n) => new()
	{
		Id = n.Id.ToString("N"),
		Position = n.Position,
		Type = n.Type,
		ControlPrev = n.ControlPrev,
		ControlNext = n.ControlNext,
		BreakAfter = n.BreakAfter,
		NewContour = n.NewContour,
		CloseAfter = n.CloseAfter
	};

	private static CurveNode FromDto(CurveNodeDto d) => new()
	{
		// Файл без идентификатора — от старой версии. Узел получает
		// новый, а привязки в таком файле всё равно ссылаются на номера.
		Id = Guid.TryParseExact(d.Id, "N", out var id) ? id : Guid.NewGuid(),
		Position = d.Position,
		Type = d.Type,
		ControlPrev = d.ControlPrev,
		ControlNext = d.ControlNext,
		BreakAfter = d.BreakAfter,
		NewContour = d.NewContour,
		CloseAfter = d.CloseAfter
	};

	private static AnchorDto ToDto(ShapeAnchor a) => new()
	{
		Enabled = a.Enabled,
		TangentToEdge = a.TangentToEdge,
		Kind = a.Kind,
		Index = a.Index,
		TargetId = a.Target?.Id.ToString("N"),
		NodeId = a.NodeId == Guid.Empty ? null : a.NodeId.ToString("N"),
		LocalU = a.LocalU,
		LocalV = a.LocalV
	};

	#endregion

	#region Текст

	private static TextDto ToDto(TextShape t) => new()
	{
		Text = t.Text,
		LocalOrigin = t.LocalOrigin,
		FontSizeMm = t.FontSizeMm,
		FontFamily = t.FontFamily,
		Bold = t.Bold,
		Italic = t.Italic,
		Underline = t.Underline,
		Strikethrough = t.Strikethrough,
		CharacterSpacingMm = t.CharacterSpacingMm,
		LineSpacingMm = t.LineSpacingMm,
		Ax = t.ax,
		Ay = t.ay,
		TangentAlong = t.tangentAlong,
		AlongShape = t.AlongShape,
		InsideOtherShape = t.InsideOtherShape,
		InsideShapeId = t.InsideShape?.Id.ToString("N"),
		AreaHAlign = t.AreaHAlign,
		AreaVAlign = t.AreaVAlign,
		WrapInsideShape = t.WrapInsideShape,
		WrapOtherShapes = t.WrapOtherShapes,
		ClipByShape = t.ClipByShape,
		Columns = t.Columns,
		DistanceBetweenColumns = t.DistanceBetweenColumns,
		Padding = t.Padding
	};

	/// <summary>
	/// Свойства текста без ссылок. Режимы AlongShape и InsideOtherShape
	/// здесь не включаются: их сеттеры смотрят на фигуру-цель, а она
	/// появится только на втором проходе.
	/// </summary>
	private static void ApplyText(TextShape t, TextDto d)
	{
		t.Text = d.Text;
		t.LocalOrigin = d.LocalOrigin;
		t.FontSizeMm = d.FontSizeMm;
		t.FontFamily = d.FontFamily;
		t.Bold = d.Bold;
		t.Italic = d.Italic;
		t.Underline = d.Underline;
		t.Strikethrough = d.Strikethrough;
		t.CharacterSpacingMm = d.CharacterSpacingMm;
		t.LineSpacingMm = d.LineSpacingMm;
		t.ax = d.Ax;
		t.ay = d.Ay;
		t.tangentAlong = d.TangentAlong;
		t.AreaHAlign = d.AreaHAlign;
		t.AreaVAlign = d.AreaVAlign;
		t.WrapInsideShape = d.WrapInsideShape;
		t.WrapOtherShapes = d.WrapOtherShapes;
		t.ClipByShape = d.ClipByShape;
		t.Columns = d.Columns;
		t.DistanceBetweenColumns = d.DistanceBetweenColumns;
		t.Padding = d.Padding;
	}

	private static TextShape FromDto(TextDto? d)
	{
		var text = new TextShape();

		if (d != null)
			ApplyText(text, d);

		return text;
	}

	#endregion

	#region Размер

	private static AngleMeasurementDto ToDto(AngleMeasurementShape a) => new()
	{
		S1P1 = a.S1P1,
		S1P2 = a.S1P2,
		S2P1 = a.S2P1,
		S2P2 = a.S2P2,
		Radius = a.Radius,
		TextAngleOffset = a.TextAngleOffset,
		InvertAngle = a.InvertAngle,
		Decimals = a.Decimals,
		ShowUnits = a.ShowUnits,
		TextOverride = a.TextOverride,
		ArrowSizeFactor = a.ArrowSizeFactor,
		TextStyle = ToDto(a.TextStyle),
		Anchors = new[]
		{
			ToDto(a.Anchors[0]),
			ToDto(a.Anchors[1]),
			ToDto(a.Anchors[2]),
			ToDto(a.Anchors[3])
		}
	};

	private static AngleMeasurementShape FromDto(AngleMeasurementDto? d)
	{
		var a = new AngleMeasurementShape();

		if (d == null)
			return a;

		a.S1P1 = d.S1P1;
		a.S1P2 = d.S1P2;
		a.S2P1 = d.S2P1;
		a.S2P2 = d.S2P2;
		a.Radius = d.Radius;
		a.TextAngleOffset = d.TextAngleOffset;
		a.InvertAngle = d.InvertAngle;
		a.Decimals = d.Decimals;
		a.ShowUnits = d.ShowUnits;
		a.TextOverride = d.TextOverride;
		a.ArrowSizeFactor = d.ArrowSizeFactor;

		if (d.TextStyle != null)
			ApplyText(a.TextStyle, d.TextStyle);

		return a;
	}

	private static MeasurementDto ToDto(MeasurementShape m) => new()
	{
		P1 = m.P1,
		P2 = m.P2,
		H = m.H,
		TextOffset = m.TextOffset,
		Decimals = m.Decimals,
		ShowUnits = m.ShowUnits,
		TextOverride = m.TextOverride,
		ArrowSizeFactor = m.ArrowSizeFactor,
		TextStyle = ToDto(m.TextStyle)
	};

	private static MeasurementShape FromDto(MeasurementDto? d)
	{
		var m = new MeasurementShape();

		if (d == null)
			return m;

		m.P1 = d.P1;
		m.P2 = d.P2;
		m.H = d.H;
		m.TextOffset = d.TextOffset;
		m.Decimals = d.Decimals;
		m.ShowUnits = d.ShowUnits;
		m.TextOverride = d.TextOverride;
		m.ArrowSizeFactor = d.ArrowSizeFactor;

		if (d.TextStyle != null)
			ApplyText(m.TextStyle, d.TextStyle);

		return m;
	}

	#endregion

	#region Изображение

	private static ImageDto ToDto(ImageShape image)
	{
		// Актуальная склейка тайлов: мазки кистью живут в них, а плоский
		// Bitmap может отставать. Без пересборки в файл ушли бы пиксели
		// до последних мазков.
		if (image.tileDoc != null)
			image.RebuildBitmapFromTiles();
		else
			image.EnsureBitmapLoaded();

		string? working = ImageCodec.Encode(image.Bitmap);
		string? original = ImageCodec.Encode(image.OriginalBitmap);

		return new ImageDto
		{
			ImagePath = image.ImagePath,
			ImageLocalRect = image.ImageLocalRect,
			Png = working,
			OriginalPng = original == working ? null : original,
			ActualResolution = image.ActualResolution,
			Adjustments = ToDto(image.Adjustments)
		};
	}

	private static void ApplyImage(ImageShape image, ImageDto? d)
	{
		if (d == null)
			return;

		image.ImagePath = d.ImagePath;
		image.ImageLocalRect = d.ImageLocalRect;
		image.ActualResolution = d.ActualResolution;

		var working = ImageCodec.Decode(d.Png);
		var original = ImageCodec.Decode(d.OriginalPng);

		// Файл без встроенных пикселей: единственная надежда — исходный путь.
		if (working == null && !string.IsNullOrWhiteSpace(d.ImagePath))
			image.EnsureBitmapLoaded();
		else if (working != null)
			image.RestoreFromDocument(working, original);

		if (d.Adjustments != null)
			ApplyAdjustments(image.Adjustments, d.Adjustments);

		image.InvalidateAdjustmentPaint();
	}

	private static ImageAdjustmentsDto ToDto(ImageAdjustmentSettings a) => new()
	{
		Brightness = a.Brightness,
		Contrast = a.Contrast,
		Exposure = a.Exposure,
		Sharpness = a.Sharpness,
		BlackWhite = a.BlackWhite,
		Shadows = a.Shadows,
		Hue = a.Hue,
		Temperature = a.Temperature,
		Tint = a.Tint,
		Vibrancy = a.Vibrancy,
		NoiseReduction = a.NoiseReduction,
		Invert = a.Invert
	};

	private static void ApplyAdjustments(ImageAdjustmentSettings a, ImageAdjustmentsDto d)
	{
		a.Brightness = d.Brightness;
		a.Contrast = d.Contrast;
		a.Exposure = d.Exposure;
		a.Sharpness = d.Sharpness;
		a.BlackWhite = d.BlackWhite;
		a.Shadows = d.Shadows;
		a.Hue = d.Hue;
		a.Temperature = d.Temperature;
		a.Tint = d.Tint;
		a.Vibrancy = d.Vibrancy;
		a.NoiseReduction = d.NoiseReduction;
		a.Invert = d.Invert;
	}

	#endregion

	#region Формат листа и единицы

	private static PaperFormatDto? ToDto(PaperFormat? f)
		=> f == null ? null : new PaperFormatDto { Title = f.title, WidthMm = f.width_mm, HeightMm = f.height_mm };

	/// <summary>
	/// Формат листа ищется в справочнике по названию и размерам.
	///
	/// Именно ищется, а не создаётся заново: списки в панели свойств
	/// сравнивают элементы по ссылке, и свежий экземпляр не совпал бы
	/// ни с одной строкой выпадающего списка.
	/// </summary>
	private static PaperFormat? FromDto(PaperFormatDto? d)
	{
		if (d == null)
			return null;

		var known = Settings.paper_formats.FirstOrDefault(p =>
			p.title == d.Title &&
			MathF.Abs(p.width_mm - d.WidthMm) < 0.01f &&
			MathF.Abs(p.height_mm - d.HeightMm) < 0.01f);

		return known ?? new PaperFormat(d.Title, d.WidthMm, d.HeightMm);
	}

	private static UoMDto? ToDto(UoM? u)
		=> u == null ? null : new UoMDto { Title = u.title, Cmm = u.cmm };

	private static UoM? FromDto(UoMDto? d)
	{
		if (d == null)
			return null;

		var known = Settings.units.FirstOrDefault(u => u.title == d.Title);
		return known ?? new UoM(d.Title, d.Cmm);
	}

	#endregion
}

/// <summary>
/// Растр внутри файла документа: PNG в base64.
///
/// PNG, а не сырые пиксели: изображение в документе обычно занимает больше
/// всего места, а base64 и так добавляет треть объёма. JPEG не годится —
/// он потерял бы альфа-канал и портил бы пиксели при каждом сохранении.
/// </summary>
public static class ImageCodec
{
	public static string? Encode(SKBitmap? bitmap)
	{
		if (bitmap == null || bitmap.Width <= 0 || bitmap.Height <= 0)
			return null;

		try
		{
			using var image = SKImage.FromBitmap(bitmap);
			using var data = image.Encode(SKEncodedImageFormat.Png, 100);

			return data == null ? null : Convert.ToBase64String(data.ToArray());
		}
		catch
		{
			// Потеря картинки не должна ронять сохранение всего документа.
			return null;
		}
	}

	public static SKBitmap? Decode(string? base64)
	{
		if (string.IsNullOrEmpty(base64))
			return null;

		try
		{
			return SKBitmap.Decode(Convert.FromBase64String(base64));
		}
		catch
		{
			return null;
		}
	}
}