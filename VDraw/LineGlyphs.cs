using SkiaSharp;
using System.Collections.Generic;
using System.Linq;

using VDraw.Animation;
using VDraw.Documents;
using VDraw.Editing;
using VDraw.Editing.Images;
using VDraw.History;
using VDraw.Shapes;
using VDraw.Ui;

namespace VDraw;

/// <summary>
/// Описание одного варианта глифа конца линии.
///
/// PreviewBitmap строится один раз при инициализации библиотеки и отдаётся
/// выпадающему списку: SkDropDownItem умеет рисовать картинку слева от текста,
/// поэтому пользователь выбирает наконечник по внешнему виду, а не по названию.
/// </summary>
public sealed class LineGlyphInfo
{
	#region Свойства

	public string Id { get; set; } = string.Empty;
	public string DisplayName { get; set; } = string.Empty;
	public SKBitmap? PreviewBitmap { get; set; }

	#endregion

	#region Методы

	public override string ToString() => DisplayName;

	#endregion
}

/// <summary>
/// Библиотека глифов концов линий.
///
/// Идентификаторы совпадают со значением, которое лежит в
/// <see cref="StrokeStyle.StartGlyph"/> и <see cref="StrokeStyle.EndGlyph"/>.
/// Прежние значения ("Arrow", "Circle", "Square", "Diamond") сохранены без
/// изменений: документы, записанные старой версией, продолжают открываться.
/// </summary>
public static class LineGlyphLibrary
{
	#region Идентификаторы

	public const string None = "";
	public const string Arrow = "Arrow";
	public const string TriangleArrow = "TriangleArrow";
	public const string EngineerArrow = "EngineerArrow";
	public const string EmptyArrow = "EmptyArrow";
	public const string Circle = "Circle";
	public const string Square = "Square";
	public const string SquareFilled = "SquareFilled";
	public const string Diamond = "Diamond";
	public const string DiamondFilled = "DiamondFilled";

	#endregion

	#region Поля

	public static readonly List<LineGlyphInfo> Items = new();

	#endregion

	#region Инициализация

	static LineGlyphLibrary()
	{
		Add(None, "None");
		Add(Arrow, "Arrow");
		Add(TriangleArrow, "Triangle arrow");
		Add(EngineerArrow, "Engineer arrow");
		Add(EmptyArrow, "Empty arrow");
		Add(Circle, "Circle");
		Add(Square, "Square");
		Add(SquareFilled, "Square filled");
		Add(Diamond, "Diamond");
		Add(DiamondFilled, "Diamond filled");
	}

	private static void Add(string id, string name)
	{
		Items.Add(new LineGlyphInfo
		{
			Id = id,
			DisplayName = name,
			PreviewBitmap = BuildPreviewBitmap(id)
		});
	}

	#endregion

	#region Поиск

	public static LineGlyphInfo? Find(string id)
	{
		id ??= string.Empty;
		return Items.FirstOrDefault(x => x.Id == id);
	}

	/// <summary>Элементы для SkDropDownBox: значение + название + картинка.</summary>
	public static IEnumerable<SkDropDownItem> ToDropDownItems()
	{
		foreach (var i in Items)
			yield return new SkDropDownItem(i.Id, i.DisplayName, i.PreviewBitmap);
	}

	#endregion

	#region Отступ линии под глиф

	/// <summary>
	/// Стрелочный ли глиф.
	///
	/// Стрелки построены остриём в узле и телом НАЗАД, вдоль линии
	/// (см. DrawFilledTriangle и EmptyArrow: вершина в нуле, основание
	/// в -s). Круг, квадрат и ромб, наоборот, отцентрованы по узлу и
	/// расходятся от него во все стороны симметрично.
	///
	/// Разница существенна: линию, доведённую до узла, стрелка своим
	/// телом закрывает, и её толщина вылезает поверх острия. У
	/// центрированных глифов такого нет — линия входит в них ровно
	/// по центру, как и задумано.
	/// </summary>
	public static bool IsArrowLike(string? id) => id is Arrow or TriangleArrow or EngineerArrow or EmptyArrow;

	/// <summary>
	/// На сколько укоротить линию с этого конца, чтобы она заканчивалась
	/// в СЕРЕДИНЕ глифа, а не в узле.
	///
	/// Половина длины стрелки: конец линии прячется под её телом, остриё
	/// остаётся чистым, а разрыва между линией и глифом не возникает даже
	/// при толстой обводке.
	///
	/// Для центрированных глифов и при отсутствии глифа отступа нет:
	/// укорачивание оставило бы видимый зазор внутри кружка или ромба.
	/// </summary>
	public static float GetLineInsetWorld(string? id, float glyphSizeWorld)
	{
		if (!IsArrowLike(id) || glyphSizeWorld <= 0f)
			return 0f;

		return glyphSizeWorld * 0.5f;
	}

	#endregion

	#region Отрисовка

	/// <summary>
	/// Рисует глиф в мировых координатах.
	///
	/// Холст поворачивается так, что ось +X совпадает с worldDirection,
	/// а начало координат — с worldPosition. Все построения ниже описаны
	/// в этой системе: остриё в нуле, хвост уходит в отрицательный X.
	/// </summary>
	public static void Draw(
		SKCanvas canvas,
		SKPoint worldPosition,
		SKPoint worldDirection,
		float glyphSizeWorld,
		string glyphName,
		SKPaint baseStrokePaint)
	{
		if (string.IsNullOrEmpty(glyphName))
			return;

		float len = MathF.Sqrt(worldDirection.X * worldDirection.X + worldDirection.Y * worldDirection.Y);
		if (len < 0.0001f)
			return;

		float ux = worldDirection.X / len;
		float uy = worldDirection.Y / len;
		float angleDeg = MathF.Atan2(uy, ux) * 180f / MathF.PI;

		// Копия кисти, чтобы не портить stroke самой линии.
		using var paint = baseStrokePaint.Clone();

		// Пунктир линии не должен попадать в наконечник: штрих разрывал бы
		// контур стрелки в произвольном месте.
		paint.PathEffect = null;

		canvas.Save();
		canvas.Translate(worldPosition);
		canvas.RotateDegrees(angleDeg);

		float s = glyphSizeWorld;

		switch (glyphName)
		{
			case Arrow:
				DrawFilledTriangle(canvas, paint, s, 0.6f);
				break;

			case TriangleArrow:
				DrawFilledTriangle(canvas, paint, s, 0.4f);
				break;

			case EngineerArrow:
				// Классический наконечник размерной линии: длина к ширине
				// примерно 3:1, поэтому полуширина равна одной шестой длины.
				DrawFilledTriangle(canvas, paint, s, 1f / 6f);
				break;

			case EmptyArrow:
				{
					paint.Style = SKPaintStyle.Stroke;
					paint.StrokeCap = SKStrokeCap.Round;
					paint.StrokeJoin = SKStrokeJoin.Round;

					using var path = new SKPath();
					path.MoveTo(-s, s * 0.5f);
					path.LineTo(0, 0);
					path.LineTo(-s, -s * 0.5f);
					canvas.DrawPath(path, paint);
					break;
				}

			case Circle:
				paint.Style = SKPaintStyle.Stroke;
				canvas.DrawCircle(0, 0, s * 0.6f, paint);
				break;

			case Square:
				DrawSquare(canvas, paint, s, filled: false);
				break;

			case SquareFilled:
				DrawSquare(canvas, paint, s, filled: true);
				break;

			case Diamond:
				DrawDiamond(canvas, paint, s, filled: false);
				break;

			case DiamondFilled:
				DrawDiamond(canvas, paint, s, filled: true);
				break;
		}

		canvas.Restore();
	}

	private static void DrawDiamond(SKCanvas canvas, SKPaint paint, float s, bool filled)
	{
		paint.Style = filled ? SKPaintStyle.Fill : SKPaintStyle.Stroke;

		using var path = new SKPath();
		float h = s * 0.7f;
		path.MoveTo(0, -h);
		path.LineTo(h, 0);
		path.LineTo(0, h);
		path.LineTo(-h, 0);
		path.Close();
		canvas.DrawPath(path, paint);
	}

	private static void DrawFilledTriangle(SKCanvas canvas, SKPaint paint, float s, float halfWidthFactor)
	{
		paint.Style = SKPaintStyle.Fill;

		using var path = new SKPath();
		path.MoveTo(0, 0);
		path.LineTo(-s, s * halfWidthFactor);
		path.LineTo(-s, -s * halfWidthFactor);
		path.Close();
		canvas.DrawPath(path, paint);
	}

	private static void DrawSquare(SKCanvas canvas, SKPaint paint, float s, bool filled)
	{
		paint.Style = filled ? SKPaintStyle.Fill : SKPaintStyle.Stroke;

		float h = s * 0.6f;
		canvas.DrawRect(-h, -h, h * 2f, h * 2f, paint);
	}

	#endregion

	#region Превью

	/// <summary>
	/// Превью для выпадающего списка: короткая линия с глифом на правом конце.
	/// Пустой идентификатор даёт линию без наконечника — это и есть вариант
	/// «нет глифа», и он должен читаться так же наглядно, как остальные.
	///
	/// Размер квадратный не случайно: SkDropDownBox вписывает картинку
	/// в квадрат PreviewBoxSize × PreviewBoxSize без сохранения пропорций.
	/// Широкое превью сжалось бы по горизонтали, и треугольная стрелка
	/// стала бы неотличима от инженерной.
	/// </summary>
	public static SKBitmap BuildPreviewBitmap(string glyphId, int width = 40, int height = 40)
	{
		var bmp = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);

		using var canvas = new SKCanvas(bmp);
		canvas.Clear(SKColors.Transparent);

		using var paint = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Stroke,
			StrokeWidth = 1.6f,
			Color = SKColors.Black,
			StrokeCap = SKStrokeCap.Butt
		};

		float y = height / 2f;
		float glyphSize = height * 0.45f;

		// Линия не доводится до самого края: остриё глифа стоит в точке tip,
		// и справа от неё нужен воздух, иначе наконечник упирается в рамку.
		float tip = width - glyphSize * 0.75f - 3f;

		canvas.DrawLine(4f, y, tip, y, paint);

		Draw(canvas, new SKPoint(tip, y), new SKPoint(1f, 0f), glyphSize, glyphId, paint);

		return bmp;
	}

	#endregion
}