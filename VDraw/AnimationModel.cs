using SkiaSharp;

using VDraw.Documents;
using VDraw.Editing;
using VDraw.Editing.Images;
using VDraw.History;
using VDraw.Shapes;
using VDraw.Ui;

namespace VDraw.Animation;

/// <summary>
/// Тип анимируемого значения.
///
/// Отдельное перечисление, а не object с проверками типов: интерполяция
/// вызывается на каждом кадре для каждой дорожки, и упаковка float в object
/// на этом пути обошлась бы дороже самой интерполяции. Заодно тип нужен
/// панели — она рисует значение и выбирает способ показа.
/// </summary>
public enum AnimValueKind
{
	Float,
	Bool,
	Color,
	Point
}

/// <summary>
/// Значение одного анимируемого параметра.
///
/// Четыре float покрывают все четыре вида: float — A, bool — A как 0/1,
/// цвет — RGBA, точка — X и Y. Структура, а не класс: ключей в документе
/// тысячи, и каждый в куче — лишняя работа сборщику мусора.
///
/// Bool интерполируется ступенькой, а не линейно: «включено на 30%»
/// смысла не имеет, поэтому значение держится до следующего ключа.
/// </summary>
public readonly struct AnimValue
{
	public AnimValueKind Kind { get; }
	public float A { get; }
	public float B { get; }
	public float C { get; }
	public float D { get; }

	private AnimValue(AnimValueKind kind, float a, float b, float c, float d)
	{
		Kind = kind;
		A = a;
		B = b;
		C = c;
		D = d;
	}

	public static AnimValue FromFloat(float v) => new(AnimValueKind.Float, v, 0f, 0f, 0f);
	public static AnimValue FromBool(bool v) => new(AnimValueKind.Bool, v ? 1f : 0f, 0f, 0f, 0f);
	public static AnimValue FromColor(SKColor c) => new(AnimValueKind.Color, c.Red, c.Green, c.Blue, c.Alpha);
	public static AnimValue FromPoint(SKPoint p) => new(AnimValueKind.Point, p.X, p.Y, 0f, 0f);

	public float AsFloat => A;
	public bool AsBool => A >= 0.5f;
	public SKPoint AsPoint => new(A, B);

	public SKColor AsColor => new(
		ToByte(A),
		ToByte(B),
		ToByte(C),
		ToByte(D));

	private static byte ToByte(float v) => (byte)Math.Clamp(MathF.Round(v), 0f, 255f);

	/// <summary>
	/// Промежуточное значение между двумя ключами.
	///
	/// Несовпадение типов возможно только если параметр сменил природу
	/// между записями ключей (например, фигуру заменили другой с тем же
	/// идентификатором параметра). Линейно смешивать такие значения нельзя,
	/// поэтому берётся ближайший ключ.
	/// </summary>
	public static AnimValue Lerp(AnimValue a, AnimValue b, float t)
	{
		if (a.Kind != b.Kind)
			return t < 1f ? a : b;

		if (a.Kind == AnimValueKind.Bool)
			return t < 1f ? a : b;

		return new AnimValue(
			a.Kind,
			a.A + (b.A - a.A) * t,
			a.B + (b.B - a.B) * t,
			a.C + (b.C - a.C) * t,
			a.D + (b.D - a.D) * t);
	}

	/// <summary>
	/// Запись значения в файл документа.
	///
	/// Одной строкой, а не объектом с полями: ключей в анимации тысячи,
	/// и объект на каждый раздул бы файл вчетверо. Формат тот же, что
	/// у остальных структур Skia в этом документе — буква вида и числа
	/// через пробел в инвариантной локали.
	/// </summary>
	public string Serialize() => Kind switch
	{
		AnimValueKind.Bool => "B " + (AsBool ? "1" : "0"),
		AnimValueKind.Color => $"C {Fmt(A)} {Fmt(B)} {Fmt(C)} {Fmt(D)}",
		AnimValueKind.Point => $"P {Fmt(A)} {Fmt(B)}",
		_ => "F " + Fmt(A)
	};

	public static bool TryParse(string? text, out AnimValue value)
	{
		value = FromFloat(0f);

		if (string.IsNullOrWhiteSpace(text))
			return false;

		var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

		if (parts.Length < 2)
			return false;

		switch (parts[0])
		{
			case "B":
				value = FromBool(Parse(parts[1]) >= 0.5f);
				return true;

			case "C":
				if (parts.Length < 5)
					return false;

				value = new AnimValue(AnimValueKind.Color, Parse(parts[1]), Parse(parts[2]), Parse(parts[3]), Parse(parts[4]));
				return true;

			case "P":
				if (parts.Length < 3)
					return false;

				value = new AnimValue(AnimValueKind.Point, Parse(parts[1]), Parse(parts[2]), 0f, 0f);
				return true;

			case "F":
				value = FromFloat(Parse(parts[1]));
				return true;

			default:
				return false;
		}
	}

	private static string Fmt(float v)
		=> v.ToString("R", System.Globalization.CultureInfo.InvariantCulture);

	private static float Parse(string s)
		=> float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : 0f;

	/// <summary>Короткая запись значения для колонки дерева параметров.</summary>
	public string Format() => Kind switch
	{
		AnimValueKind.Bool => AsBool ? "on" : "off",
		AnimValueKind.Point => $"{A:0.##}; {B:0.##}",
		AnimValueKind.Color => $"#{ToByte(D):X2}{ToByte(A):X2}{ToByte(B):X2}{ToByte(C):X2}",
		_ => A.ToString("0.###")
	};
}

/// <summary>
/// Один анимируемый параметр фигуры: как его прочитать и как записать.
///
/// Делегаты, а не отражение по имени свойства. Причин две. Во-первых,
/// параметры не сводятся к свойствам: узел кривой — это элемент списка,
/// а точка тени требует попутно поднять флаг OffsetsInitialized, иначе
/// первая же отрисовка перезапишет её значением по умолчанию. Во-вторых,
/// отражение на каждом кадре для каждой дорожки — заметная цена.
///
/// Делегаты захватывают КОНКРЕТНЫЙ экземпляр фигуры, поэтому набор
/// параметров живёт не дольше этого экземпляра и пересобирается через
/// AnimParamCatalog. В ключах хранится только строковый Id, который
/// замену экземпляра переживает.
/// </summary>
public sealed class AnimParam
{
	private readonly Func<AnimValue> _get;
	private readonly Action<AnimValue> _set;

	public string Id { get; }
	public string Title { get; }
	public AnimValueKind Kind { get; }

	public AnimParam(string id, string title, AnimValueKind kind, Func<AnimValue> get, Action<AnimValue> set)
	{
		Id = id;
		Title = title;
		Kind = kind;
		_get = get;
		_set = set;
	}

	public AnimValue Get() => _get();

	public void Set(AnimValue value) => _set(value);

	public static AnimParam Float(string id, string title, Func<float> get, Action<float> set)
		=> new(id, title, AnimValueKind.Float,
			() => AnimValue.FromFloat(get()),
			v => set(v.AsFloat));

	public static AnimParam Int(string id, string title, Func<int> get, Action<int> set)
		=> new(id, title, AnimValueKind.Float,
			() => AnimValue.FromFloat(get()),
			v => set((int)MathF.Round(v.AsFloat)));

	public static AnimParam Bool(string id, string title, Func<bool> get, Action<bool> set)
		=> new(id, title, AnimValueKind.Bool,
			() => AnimValue.FromBool(get()),
			v => set(v.AsBool));

	public static AnimParam Color(string id, string title, Func<SKColor> get, Action<SKColor> set)
		=> new(id, title, AnimValueKind.Color,
			() => AnimValue.FromColor(get()),
			v => set(v.AsColor));

	public static AnimParam Point(string id, string title, Func<SKPoint> get, Action<SKPoint> set)
		=> new(id, title, AnimValueKind.Point,
			() => AnimValue.FromPoint(get()),
			v => set(v.AsPoint));
}

/// <summary>
/// Узел дерева параметров: либо группа (Stroke, Fill, Nodes...), либо
/// сам параметр. Дерево строится под конкретную фигуру и отдаётся панели
/// для показа в левой части.
/// </summary>
public sealed class AnimParamNode
{
	public string Id { get; }
	public string Title { get; }
	public AnimParam? Param { get; }
	public List<AnimParamNode> Children { get; } = new();

	public bool IsGroup => Param == null;

	public AnimParamNode(string id, string title, AnimParam? param = null)
	{
		Id = id;
		Title = title;
		Param = param;
	}

	public AnimParamNode AddGroup(string id, string title)
	{
		var group = new AnimParamNode(id, title);
		Children.Add(group);
		return group;
	}

	public void AddParam(AnimParam param)
		=> Children.Add(new AnimParamNode(param.Id, param.Title, param));

	/// <summary>Все параметры поддерева, включая собственный.</summary>
	public IEnumerable<AnimParam> EnumerateParams()
	{
		if (Param != null)
			yield return Param;

		foreach (var child in Children)
		{
			foreach (var p in child.EnumerateParams())
				yield return p;
		}
	}

	/// <summary>Пустые группы прячутся: у линии нет узлов, у не-Morthing нет Morthing.</summary>
	public void RemoveEmptyGroups()
	{
		for (int i = Children.Count - 1; i >= 0; i--)
		{
			var child = Children[i];
			child.RemoveEmptyGroups();

			if (child.IsGroup && child.Children.Count == 0)
				Children.RemoveAt(i);
		}
	}
}

/// <summary>
/// Сборка дерева анимируемых параметров по фигуре.
///
/// Состав групп: Transform, Nodes, Stroke, Fill, Shadow, Transparency,
/// Morthing. Enabled есть у Stroke, Fill, Shadow и Transparency своим
/// полем; у Morthing собственного флага нет, и его роль исполняет
/// IsVisible инструмента преобразования — именно он управляет отрисовкой
/// цепочки шагов.
/// </summary>
public static class AnimParamCatalog
{
	/// <summary>
	/// Предел числа узлов, попадающих в дерево. Кривая после трассировки
	/// растрового контура может иметь их тысячи, и дерево из тысяч строк
	/// не столько показывает, сколько мешает.
	/// </summary>
	public const int MaxNodes = 300;

	public static AnimParamNode Build(Shape shape)
	{
		var root = new AnimParamNode(string.Empty, ShapeTitle(shape));

		BuildTransform(root, shape);
		BuildNodes(root, shape);
		BuildStroke(root, shape);
		BuildFill(root, shape);
		BuildShadow(root, shape);
		BuildTransparency(root, shape);
		BuildMorthing(root, shape);

		root.RemoveEmptyGroups();
		return root;
	}

	public static Dictionary<string, AnimParam> BuildParamMap(Shape shape)
	{
		var map = new Dictionary<string, AnimParam>(StringComparer.Ordinal);

		foreach (var p in Build(shape).EnumerateParams())
			map[p.Id] = p;

		return map;
	}

	private static string ShapeTitle(Shape shape)
	{
		string name = string.IsNullOrWhiteSpace(shape.Name) ? shape.GetType().Name : shape.Name;
		return name;
	}

	#region Transform

	/// <summary>
	/// Трансформация как шесть компонент матрицы.
	///
	/// Не «сдвиг, поворот, масштаб»: разложение аффинной матрицы на них
	/// неоднозначно, и запись ключа с последующим восстановлением давала бы
	/// не ту матрицу, что была. Компоненты же интерполируются линейно
	/// и точно, а привычные X и Y — это TransX и TransY, то есть ровно те
	/// поля, которые правит панель свойств.
	/// </summary>
	private static void BuildTransform(AnimParamNode root, Shape shape)
	{
		var g = root.AddGroup("transform", "Transform");

		g.AddParam(AnimParam.Float("transform.x", "X",
			() => shape.LocalTransform.TransX,
			v => { var m = shape.LocalTransform; m.TransX = v; shape.LocalTransform = m; }));

		g.AddParam(AnimParam.Float("transform.y", "Y",
			() => shape.LocalTransform.TransY,
			v => { var m = shape.LocalTransform; m.TransY = v; shape.LocalTransform = m; }));

		g.AddParam(AnimParam.Float("transform.scaleX", "Scale X",
			() => shape.LocalTransform.ScaleX,
			v => { var m = shape.LocalTransform; m.ScaleX = v; shape.LocalTransform = m; }));

		g.AddParam(AnimParam.Float("transform.scaleY", "Scale Y",
			() => shape.LocalTransform.ScaleY,
			v => { var m = shape.LocalTransform; m.ScaleY = v; shape.LocalTransform = m; }));

		g.AddParam(AnimParam.Float("transform.skewX", "Skew X",
			() => shape.LocalTransform.SkewX,
			v => { var m = shape.LocalTransform; m.SkewX = v; shape.LocalTransform = m; }));

		g.AddParam(AnimParam.Float("transform.skewY", "Skew Y",
			() => shape.LocalTransform.SkewY,
			v => { var m = shape.LocalTransform; m.SkewY = v; shape.LocalTransform = m; }));

		// У инструмента преобразования этот же флаг выставлен группой
		// Morthing как её Enabled. Два параметра на одно поле означали бы
		// две дорожки, спорящие за него, поэтому здесь он пропускается.
		if (shape is not TransformShape)
		{
			g.AddParam(AnimParam.Bool("transform.visible", "Visible",
				() => shape.IsVisible,
				v => shape.IsVisible = v));
		}
	}

	#endregion

	#region Nodes

	private static void BuildNodes(AnimParamNode root, Shape shape)
	{
		var g = root.AddGroup("nodes", "Nodes");

		switch (shape)
		{
			case CurveShape curve:
				BuildCurveNodes(g, curve);
				break;

			case RoundedRectangleShape rr:
				AddRectParams(g, () => rr.LocalRect, r => rr.LocalRect = r);
				g.AddParam(AnimParam.Float("nodes.radiusX", "Radius X", () => rr.RadiusX, v => rr.RadiusX = v));
				g.AddParam(AnimParam.Float("nodes.radiusY", "Radius Y", () => rr.RadiusY, v => rr.RadiusY = v));
				break;

			case ChamferedRectangleShape cr:
				AddRectParams(g, () => cr.LocalRect, r => cr.LocalRect = r);
				g.AddParam(AnimParam.Float("nodes.chamferX", "Chamfer X", () => cr.ChamferX, v => cr.ChamferX = v));
				g.AddParam(AnimParam.Float("nodes.chamferY", "Chamfer Y", () => cr.ChamferY, v => cr.ChamferY = v));
				break;

			case RectangleShape rect:
				AddRectParams(g, () => rect.LocalRect, r => rect.LocalRect = r);
				break;

			case Circle2PShape c2:
				g.AddParam(AnimParam.Point("nodes.center", "Center",
					() => c2.LocalCenter,
					v =>
					{
						// Точки построения едут за формой: по ним рисуются
						// ручки, и оставь их на месте — ручки оторвутся
						// от фигуры.
						var delta = new SKPoint(v.X - c2.LocalCenter.X, v.Y - c2.LocalCenter.Y);
						c2.LocalCenter = v;
						c2.CenterPoint = new SKPoint(c2.CenterPoint.X + delta.X, c2.CenterPoint.Y + delta.Y);
						c2.RadiusPoint = new SKPoint(c2.RadiusPoint.X + delta.X, c2.RadiusPoint.Y + delta.Y);
					}));

				g.AddParam(AnimParam.Float("nodes.radius", "Radius",
					() => c2.Radius,
					v =>
					{
						c2.Radius = v;
						c2.RadiusPoint = ScaleFrom(c2.CenterPoint, c2.RadiusPoint, v);
					}));
				break;

			case Circle3PShape c3:
				g.AddParam(AnimParam.Point("nodes.p1", "Point 1", () => c3.P1, v => c3.P1 = v));
				g.AddParam(AnimParam.Point("nodes.p2", "Point 2", () => c3.P2, v => c3.P2 = v));
				g.AddParam(AnimParam.Point("nodes.p3", "Point 3", () => c3.P3, v => c3.P3 = v));
				break;

			case Ellipse3PShape e3:
				g.AddParam(AnimParam.Point("nodes.center", "Center",
					() => e3.LocalCenter,
					v =>
					{
						var delta = new SKPoint(v.X - e3.LocalCenter.X, v.Y - e3.LocalCenter.Y);
						e3.LocalCenter = v;
						e3.CenterPoint = new SKPoint(e3.CenterPoint.X + delta.X, e3.CenterPoint.Y + delta.Y);
						e3.RadiusPoint1 = new SKPoint(e3.RadiusPoint1.X + delta.X, e3.RadiusPoint1.Y + delta.Y);
						e3.RadiusPoint2 = new SKPoint(e3.RadiusPoint2.X + delta.X, e3.RadiusPoint2.Y + delta.Y);
					}));

				g.AddParam(AnimParam.Float("nodes.radiusX", "Radius X", () => e3.RadiusX, v => e3.RadiusX = v));
				g.AddParam(AnimParam.Float("nodes.radiusY", "Radius Y", () => e3.RadiusY, v => e3.RadiusY = v));
				break;

			case Ellipse5PShape e5:
				g.AddParam(AnimParam.Point("nodes.center", "Center", () => e5.LocalCenter, v => e5.LocalCenter = v));
				g.AddParam(AnimParam.Float("nodes.radiusX", "Radius X", () => e5.RadiusX, v => e5.RadiusX = v));
				g.AddParam(AnimParam.Float("nodes.radiusY", "Radius Y", () => e5.RadiusY, v => e5.RadiusY = v));
				AddPointListParams(g, e5.Points);
				break;

			case PolygonShape poly:
				if (poly.UseExplicitPoints)
				{
					AddPointListParams(g, poly.Points);
				}
				else
				{
					g.AddParam(AnimParam.Point("nodes.center", "Center", () => poly.LocalCenter, v => poly.LocalCenter = v));
					g.AddParam(AnimParam.Float("nodes.radiusX", "Radius X", () => poly.RadiusX, v => poly.RadiusX = v));
					g.AddParam(AnimParam.Float("nodes.radiusY", "Radius Y", () => poly.RadiusY, v => poly.RadiusY = v));
					g.AddParam(AnimParam.Float("nodes.rotation", "Rotation, rad", () => poly.RotationRad, v => poly.RotationRad = v));
					g.AddParam(AnimParam.Int("nodes.sides", "Sides", () => poly.Sides, v => poly.Sides = Math.Max(3, v)));
				}
				break;

			case MeasurementShape ms:
				g.AddParam(AnimParam.Point("nodes.p1", "Point 1", () => ms.P1, v => ms.P1 = v));
				g.AddParam(AnimParam.Point("nodes.p2", "Point 2", () => ms.P2, v => ms.P2 = v));
				g.AddParam(AnimParam.Float("nodes.h", "Extension", () => ms.H, v => ms.H = v));
				g.AddParam(AnimParam.Float("nodes.textOffset", "Text offset", () => ms.TextOffset, v => ms.TextOffset = v));
				break;
		}
	}

	private static void BuildCurveNodes(AnimParamNode g, CurveShape curve)
	{
		int count = Math.Min(curve.Nodes.Count, MaxNodes);

		for (int i = 0; i < count; i++)
		{
			int index = i;
			var node = curve.Nodes[index];

			g.AddParam(AnimParam.Point($"nodes.{index}.pos", $"Node {index + 1}",
				() => NodeAt(curve, index)?.Position ?? SKPoint.Empty,
				v =>
				{
					var n = NodeAt(curve, index);
					if (n != null)
						n.Position = v;
				}));

			// Контрольные точки заводятся только у тех узлов, у которых они
			// есть. Присвоить их узлу, который их не имел, значит превратить
			// прямой сегмент в кривую — не то, что ожидается от ключа.
			if (node.HasPrevControl)
			{
				g.AddParam(AnimParam.Point($"nodes.{index}.cprev", $"Node {index + 1} ctrl in",
					() => NodeAt(curve, index)?.ControlPrev ?? SKPoint.Empty,
					v =>
					{
						var n = NodeAt(curve, index);
						if (n != null && n.HasPrevControl)
							n.ControlPrev = v;
					}));
			}

			if (node.HasNextControl)
			{
				g.AddParam(AnimParam.Point($"nodes.{index}.cnext", $"Node {index + 1} ctrl out",
					() => NodeAt(curve, index)?.ControlNext ?? SKPoint.Empty,
					v =>
					{
						var n = NodeAt(curve, index);
						if (n != null && n.HasNextControl)
							n.ControlNext = v;
					}));
			}
		}
	}

	/// <summary>
	/// Узел по индексу с проверкой границ.
	///
	/// Список узлов правят инструменты: вставка точки, разрезание, булевы
	/// операции. Дорожка, записанная до такой правки, может ссылаться
	/// на индекс, которого больше нет, и обращение по нему уронило бы
	/// воспроизведение.
	/// </summary>
	private static CurveNode? NodeAt(CurveShape curve, int index)
		=> index >= 0 && index < curve.Nodes.Count ? curve.Nodes[index] : null;

	private static void AddPointListParams(AnimParamNode g, List<SKPoint> points)
	{
		int count = Math.Min(points.Count, MaxNodes);

		for (int i = 0; i < count; i++)
		{
			int index = i;

			g.AddParam(AnimParam.Point($"nodes.{index}.pos", $"Point {index + 1}",
				() => index < points.Count ? points[index] : SKPoint.Empty,
				v =>
				{
					if (index < points.Count)
						points[index] = v;
				}));
		}
	}

	private static void AddRectParams(AnimParamNode g, Func<SKRect> get, Action<SKRect> set)
	{
		g.AddParam(AnimParam.Float("nodes.left", "Left",
			() => get().Left,
			v => { var r = get(); set(new SKRect(v, r.Top, r.Right, r.Bottom)); }));

		g.AddParam(AnimParam.Float("nodes.top", "Top",
			() => get().Top,
			v => { var r = get(); set(new SKRect(r.Left, v, r.Right, r.Bottom)); }));

		g.AddParam(AnimParam.Float("nodes.right", "Right",
			() => get().Right,
			v => { var r = get(); set(new SKRect(r.Left, r.Top, v, r.Bottom)); }));

		g.AddParam(AnimParam.Float("nodes.bottom", "Bottom",
			() => get().Bottom,
			v => { var r = get(); set(new SKRect(r.Left, r.Top, r.Right, v)); }));
	}

	/// <summary>Точка на луче from→to, отодвинутая от from на длину length.</summary>
	private static SKPoint ScaleFrom(SKPoint from, SKPoint to, float length)
	{
		float dx = to.X - from.X;
		float dy = to.Y - from.Y;
		float len = MathF.Sqrt(dx * dx + dy * dy);

		if (len < 0.0001f)
			return new SKPoint(from.X + length, from.Y);

		return new SKPoint(from.X + dx / len * length, from.Y + dy / len * length);
	}

	#endregion

	#region Оформление

	private static void BuildStroke(AnimParamNode root, Shape shape)
	{
		if (shape is GroupShape)
			return;

		var stroke = shape.Stroke;
		if (stroke == null)
			return;

		var g = root.AddGroup("stroke", "Stroke");

		g.AddParam(AnimParam.Bool("stroke.enabled", "Enabled", () => stroke.Enabled, v => stroke.Enabled = v));
		g.AddParam(AnimParam.Color("stroke.color", "Color", () => stroke.Color, v => stroke.Color = v));
		g.AddParam(AnimParam.Float("stroke.thickness", "Thickness", () => stroke.Thickness, v => stroke.Thickness = MathF.Max(0f, v)));
		g.AddParam(AnimParam.Float("stroke.patternLength", "Pattern length", () => stroke.PatternLength, v => stroke.PatternLength = MathF.Max(0f, v)));
		g.AddParam(AnimParam.Float("stroke.startGlyphSize", "Start glyph size", () => stroke.StartGlyphSize, v => stroke.StartGlyphSize = MathF.Max(0f, v)));
		g.AddParam(AnimParam.Float("stroke.endGlyphSize", "End glyph size", () => stroke.EndGlyphSize, v => stroke.EndGlyphSize = MathF.Max(0f, v)));
	}

	private static void BuildFill(AnimParamNode root, Shape shape)
	{
		if (shape is GroupShape)
			return;

		var fill = shape.Fill;
		if (fill == null)
			return;

		var g = root.AddGroup("fill", "Fill");

		g.AddParam(AnimParam.Bool("fill.enabled", "Enabled", () => fill.Enabled, v => fill.Enabled = v));
		g.AddParam(AnimParam.Color("fill.solidColor", "Color", () => fill.SolidColor, v => fill.SolidColor = v));
		g.AddParam(AnimParam.Color("fill.gradientColor1", "Gradient color 1", () => fill.GradientColor1, v => fill.GradientColor1 = v));
		g.AddParam(AnimParam.Color("fill.gradientColor2", "Gradient color 2", () => fill.GradientColor2, v => fill.GradientColor2 = v));
		g.AddParam(AnimParam.Float("fill.angle", "Angle", () => fill.Angle, v => fill.Angle = v));

		// Точки заливки поднимают флаг инициализации: пока он снят, первая
		// же отрисовка выводит их из габаритов и затирает записанное.
		g.AddParam(AnimParam.Point("fill.startPoint", "Start point",
			() => fill.StartPoint,
			v => { fill.StartPoint = v; fill.FillTransformInitialized = true; }));

		g.AddParam(AnimParam.Point("fill.endPoint", "End point",
			() => fill.EndPoint,
			v => { fill.EndPoint = v; fill.FillTransformInitialized = true; }));

		g.AddParam(AnimParam.Color("fill.patternFront", "Pattern front", () => fill.PatternFrontColor, v => fill.PatternFrontColor = v));
		g.AddParam(AnimParam.Color("fill.patternBack", "Pattern back", () => fill.PatternBackColor, v => fill.PatternBackColor = v));

		var stops = fill.Stops;
		int stopCount = Math.Min(stops.Count, 32);

		if (stopCount > 0)
		{
			var sg = g.AddGroup("fill.stops", "Gradient stops");

			for (int i = 0; i < stopCount; i++)
			{
				int index = i;

				sg.AddParam(AnimParam.Float($"fill.stops.{index}.offset", $"Stop {index + 1} offset",
					() => index < stops.Count ? stops[index].Offset : 0f,
					v =>
					{
						if (index < stops.Count)
							stops[index].Offset = Math.Clamp(v, 0f, 1f);
					}));

				sg.AddParam(AnimParam.Color($"fill.stops.{index}.color", $"Stop {index + 1} color",
					() => index < stops.Count ? stops[index].Color : SKColors.Black,
					v =>
					{
						if (index < stops.Count)
							stops[index].Color = v;
					}));
			}
		}
	}

	private static void BuildShadow(AnimParamNode root, Shape shape)
	{
		if (shape is GroupShape)
			return;

		var shadow = shape.Shadow;
		if (shadow == null)
			return;

		var g = root.AddGroup("shadow", "Shadow");

		g.AddParam(AnimParam.Bool("shadow.enabled", "Enabled", () => shadow.Enabled, v => shadow.Enabled = v));
		g.AddParam(AnimParam.Color("shadow.color", "Color", () => shadow.Color, v => shadow.Color = v));
		g.AddParam(AnimParam.Float("shadow.softness", "Softness", () => shadow.Softness, v => shadow.Softness = Math.Clamp(v, 0f, 1f)));

		g.AddParam(AnimParam.Point("shadow.centerOffset", "Center offset",
			() => shadow.CenterOffset,
			v => { shadow.CenterOffset = v; shadow.OffsetsInitialized = true; }));

		g.AddParam(AnimParam.Point("shadow.sizeVector", "Size vector",
			() => shadow.SizeVector,
			v => { shadow.SizeVector = v; shadow.OffsetsInitialized = true; }));
	}

	private static void BuildTransparency(AnimParamNode root, Shape shape)
	{
		if (shape is GroupShape)
			return;

		var tr = shape.Transparency;
		if (tr == null)
			return;

		var g = root.AddGroup("transparency", "Transparency");

		g.AddParam(AnimParam.Bool("transparency.enabled", "Enabled", () => tr.Enabled, v => tr.Enabled = v));
		g.AddParam(AnimParam.Float("transparency.startOpacity", "Start opacity", () => tr.StartOpacity, v => tr.StartOpacity = Math.Clamp(v, 0f, 1f)));
		g.AddParam(AnimParam.Float("transparency.endOpacity", "End opacity", () => tr.EndOpacity, v => tr.EndOpacity = Math.Clamp(v, 0f, 1f)));

		g.AddParam(AnimParam.Point("transparency.startOffset", "Start offset",
			() => tr.StartOffset,
			v => { tr.StartOffset = v; tr.OffsetsInitialized = true; }));

		g.AddParam(AnimParam.Point("transparency.endOffset", "End offset",
			() => tr.EndOffset,
			v => { tr.EndOffset = v; tr.OffsetsInitialized = true; }));
	}

	/// <summary>
	/// Пошаговое преобразование. Собственного Enabled у инструмента нет,
	/// поэтому включением служит IsVisible — единственный флаг, который
	/// действительно убирает цепочку шагов с холста.
	/// </summary>
	private static void BuildMorthing(AnimParamNode root, Shape shape)
	{
		if (shape is not TransformShape morth)
			return;

		var g = root.AddGroup("morth", "Morthing");

		g.AddParam(AnimParam.Bool("morth.enabled", "Enabled", () => morth.IsVisible, v => morth.IsVisible = v));
		g.AddParam(AnimParam.Int("morth.steps", "Steps", () => morth.Steps, v => morth.Steps = v));
		g.AddParam(AnimParam.Float("morth.bias", "Bias", () => morth.Bias, v => morth.Bias = Math.Clamp(v, 0f, 1f)));
		g.AddParam(AnimParam.Point("morth.p1", "Start point", () => morth.P1, v => morth.P1 = v));
		g.AddParam(AnimParam.Point("morth.p2", "End point", () => morth.P2, v => morth.P2 = v));
	}

	#endregion
}

/// <summary>Ключ анимации: значение параметра на конкретном кадре.</summary>
public sealed class AnimKey
{
	public int Frame { get; set; }
	public AnimValue Value { get; set; }

	public AnimKey(int frame, AnimValue value)
	{
		Frame = frame;
		Value = value;
	}
}

/// <summary>
/// Дорожка одного параметра одной фигуры. Ключи хранятся упорядоченными
/// по кадру: поиск соседей вокруг текущего кадра — самая частая операция,
/// и на отсортированном списке он линеен без предварительной сортировки.
/// </summary>
public sealed class AnimTrack
{
	public string ParamId { get; }
	public string Title { get; set; }
	public List<AnimKey> Keys { get; } = new();

	public AnimTrack(string paramId, string title)
	{
		ParamId = paramId;
		Title = title;
	}

	public AnimKey? Find(int frame)
	{
		foreach (var k in Keys)
		{
			if (k.Frame == frame)
				return k;
		}

		return null;
	}

	public void Set(int frame, AnimValue value)
	{
		var existing = Find(frame);

		if (existing != null)
		{
			existing.Value = value;
			return;
		}

		Keys.Add(new AnimKey(frame, value));
		Keys.Sort(static (a, b) => a.Frame.CompareTo(b.Frame));
	}

	public bool Remove(int frame)
	{
		for (int i = 0; i < Keys.Count; i++)
		{
			if (Keys[i].Frame == frame)
			{
				Keys.RemoveAt(i);
				return true;
			}
		}

		return false;
	}

	/// <summary>
	/// Переносит ключ на другой кадр. Занятый кадр перезаписывается:
	/// два ключа одного параметра на одном кадре смысла не имеют.
	/// </summary>
	public bool Move(int from, int to)
	{
		if (from == to)
			return false;

		var key = Find(from);
		if (key == null)
			return false;

		var occupied = Find(to);
		if (occupied != null)
			Keys.Remove(occupied);

		key.Frame = to;
		Keys.Sort(static (a, b) => a.Frame.CompareTo(b.Frame));
		return true;
	}

	/// <summary>
	/// Значение параметра на кадре.
	///
	/// За пределами крайних ключей значение держится постоянным, а не
	/// экстраполируется: продолжение движения за последним ключом
	/// выглядело бы как ошибка.
	/// </summary>
	public bool TryEvaluate(int frame, out AnimValue value)
	{
		value = default;

		if (Keys.Count == 0)
			return false;

		if (frame <= Keys[0].Frame)
		{
			value = Keys[0].Value;
			return true;
		}

		var last = Keys[^1];

		if (frame >= last.Frame)
		{
			value = last.Value;
			return true;
		}

		for (int i = 0; i < Keys.Count - 1; i++)
		{
			var a = Keys[i];
			var b = Keys[i + 1];

			if (frame < a.Frame || frame > b.Frame)
				continue;

			int span = b.Frame - a.Frame;
			float t = span <= 0 ? 0f : (frame - a.Frame) / (float)span;

			value = AnimValue.Lerp(a.Value, b.Value, t);
			return true;
		}

		value = last.Value;
		return true;
	}

	public void ClampTo(int frameCount)
	{
		for (int i = Keys.Count - 1; i >= 0; i--)
		{
			if (Keys[i].Frame <= frameCount)
				continue;

			// Ключ за границей не выбрасывается, а притягивается к последнему
			// кадру: уменьшение длины анимации не должно уничтожать работу.
			if (Find(frameCount) != null)
				Keys.RemoveAt(i);
			else
				Keys[i].Frame = frameCount;
		}

		Keys.Sort(static (a, b) => a.Frame.CompareTo(b.Frame));
	}
}

/// <summary>Все дорожки одной фигуры.</summary>
public sealed class ShapeAnimation
{
	public Guid ShapeId { get; }
	public Dictionary<string, AnimTrack> Tracks { get; } = new(StringComparer.Ordinal);

	public ShapeAnimation(Guid shapeId)
	{
		ShapeId = shapeId;
	}

	public bool HasAnyKeys
	{
		get
		{
			foreach (var t in Tracks.Values)
			{
				if (t.Keys.Count > 0)
					return true;
			}

			return false;
		}
	}

	public AnimTrack GetOrCreate(string paramId, string title)
	{
		if (Tracks.TryGetValue(paramId, out var track))
			return track;

		track = new AnimTrack(paramId, title);
		Tracks[paramId] = track;
		return track;
	}

	public AnimTrack? Find(string paramId)
		=> Tracks.TryGetValue(paramId, out var t) ? t : null;

	public void DropEmpty()
	{
		var empty = new List<string>();

		foreach (var pair in Tracks)
		{
			if (pair.Value.Keys.Count == 0)
				empty.Add(pair.Key);
		}

		foreach (var id in empty)
			Tracks.Remove(id);
	}
}

/// <summary>
/// Анимация страницы: длина в кадрах, текущий кадр и дорожки по фигурам.
///
/// Живёт в Page, а не в панели: панель — представление, её можно закрыть
/// и открыть заново, а ключи принадлежат документу. Привязка по Guid
/// фигуры, а не по ссылке, — по той же причине, по которой так устроены
/// привязки инструмента преобразования: экземпляр фигуры заменяется
/// при переводе в кривую и булевых операциях, идентификатор — нет.
///
/// Сериализация ключей в файл документа пока не сделана: DocumentMapper
/// о них не знает, и при сохранении они теряются.
/// </summary>
public sealed class PageAnimation
{
	public const int MinFrameCount = 1;
	public const int MaxFrameCount = 10000;

	private int _frameCount = 25;
	private int _currentFrame = 1;

	private readonly Dictionary<Guid, ShapeAnimation> _shapes = new();

	public IReadOnlyDictionary<Guid, ShapeAnimation> Shapes => _shapes;

	public const int MinFps = 1;
	public const int MaxFps = 120;

	private int _fps = DefaultFps;

	/// <summary>Кадров в секунде по умолчанию — как у стандарта PAL.</summary>
	public const int DefaultFps = 25;

	/// <summary>Кадров в секунде при воспроизведении.</summary>
	public int Fps
	{
		get => _fps;
		set => _fps = Math.Clamp(value, MinFps, MaxFps);
	}

	/// <summary>Длина анимации в кадрах. Кадры нумеруются с единицы.</summary>
	public int FrameCount
	{
		get => _frameCount;
		set
		{
			int v = Math.Clamp(value, MinFrameCount, MaxFrameCount);

			if (v == _frameCount)
				return;

			_frameCount = v;

			foreach (var shape in _shapes.Values)
			{
				foreach (var track in shape.Tracks.Values)
					track.ClampTo(_frameCount);
			}

			CurrentFrame = _currentFrame;
		}
	}

	public int CurrentFrame
	{
		get => _currentFrame;
		set => _currentFrame = Math.Clamp(value, MinFrameCount, _frameCount);
	}

	/// <summary>
	/// Подтягивает ключи, оказавшиеся за концом анимации, к последнему
	/// кадру. Нужно после загрузки файла: сеттер FrameCount делает это сам,
	/// но только когда длина действительно меняется.
	/// </summary>
	public void ClampKeysToLength()
	{
		foreach (var shape in _shapes.Values)
		{
			foreach (var track in shape.Tracks.Values)
				track.ClampTo(_frameCount);
		}
	}

	public ShapeAnimation GetOrCreate(Guid shapeId)
	{
		if (_shapes.TryGetValue(shapeId, out var anim))
			return anim;

		anim = new ShapeAnimation(shapeId);
		_shapes[shapeId] = anim;
		return anim;
	}

	public ShapeAnimation? Find(Guid shapeId)
		=> _shapes.TryGetValue(shapeId, out var a) ? a : null;

	public void Remove(Guid shapeId) => _shapes.Remove(shapeId);

	public void Clear() => _shapes.Clear();

	/// <summary>Есть ли хоть один ключ на этом кадре — для отметок на линейке.</summary>
	public bool HasAnyKeyAt(int frame)
	{
		foreach (var shape in _shapes.Values)
		{
			foreach (var track in shape.Tracks.Values)
			{
				if (track.Find(frame) != null)
					return true;
			}
		}

		return false;
	}

	/// <summary>
	/// Записывает текущие значения указанных параметров как ключи на кадре.
	/// Пустой перечень параметров — ничего не делает; выбор «все или
	/// отдельные» остаётся за вызывающим.
	/// </summary>
	public int SetKeys(Shape shape, IEnumerable<AnimParam> parameters, int frame)
	{
		frame = Math.Clamp(frame, MinFrameCount, _frameCount);

		var anim = GetOrCreate(shape.Id);
		int written = 0;

		foreach (var p in parameters)
		{
			anim.GetOrCreate(p.Id, p.Title).Set(frame, p.Get());
			written++;
		}

		return written;
	}

	public int RemoveKey(Guid shapeId, string paramId, int frame)
	{
		var anim = Find(shapeId);
		var track = anim?.Find(paramId);

		if (track == null)
			return 0;

		if (!track.Remove(frame))
			return 0;

		if (track.Keys.Count == 0)
			anim!.DropEmpty();

		return 1;
	}

	/// <summary>Значение ключа, если он есть на этом кадре.</summary>
	public AnimValue? GetKeyValue(Guid shapeId, string paramId, int frame)
		=> Find(shapeId)?.Find(paramId)?.Find(frame)?.Value;

	/// <summary>
	/// Единственная точка правки одного ключа: null стирает его, значение
	/// ставит или перезаписывает.
	///
	/// Через неё же работает отмена — ей достаточно подать прежнее
	/// состояние ячейки. Поэтому запись и удаление ключа не имеют
	/// отдельных обратных операций, которые пришлось бы держать
    /// в согласии друг с другом.
	/// </summary>
	public void ApplyEdit(Guid shapeId, string paramId, string title, int frame, AnimValue? value)
	{
		if (value.HasValue)
		{
			GetOrCreate(shapeId).GetOrCreate(paramId, title).Set(Math.Clamp(frame, MinFrameCount, _frameCount), value.Value);
			return;
		}

		RemoveKey(shapeId, paramId, frame);
	}

	/// <summary>
	/// Раскладывает значения дорожек на фигуры страницы.
	///
	/// Карта параметров собирается только для фигур, у которых есть ключи:
	/// сборка дерева на каждую фигуру каждый кадр стоила бы заметно дороже
	/// самой интерполяции.
	/// </summary>
	public void ApplyFrame(Page? page, int frame)
	{
		if (page == null || _shapes.Count == 0)
			return;

		frame = Math.Clamp(frame, MinFrameCount, _frameCount);

		foreach (var shape in EnumerateShapes(page))
		{
			var anim = Find(shape.Id);

			if (anim == null || !anim.HasAnyKeys)
				continue;

			var map = AnimParamCatalog.BuildParamMap(shape);

			foreach (var track in anim.Tracks.Values)
			{
				if (track.Keys.Count == 0)
					continue;

				if (!map.TryGetValue(track.ParamId, out var param))
					continue;

				if (track.TryEvaluate(frame, out var value))
					param.Set(value);
			}
		}
	}

	/// <summary>
	/// Фигуры страницы, включая вложенные в группы. Направляющие
	/// пропускаются: это разметка листа, а не рисунок.
	/// </summary>
	public static IEnumerable<Shape> EnumerateShapes(Page page)
	{
		foreach (var shape in page.Shapes)
		{
			foreach (var s in Expand(shape))
				yield return s;
		}
	}

	private static IEnumerable<Shape> Expand(Shape shape)
	{
		if (shape is GuideLine)
			yield break;

		yield return shape;

		if (shape is not GroupShape group)
			yield break;

		foreach (var child in group.Children)
		{
			foreach (var s in Expand(child))
				yield return s;
		}
	}
}

/// <summary>
/// Правка одной ячейки «параметр × кадр»: что там было и что стало.
/// null с любой стороны означает отсутствие ключа, поэтому одной
/// структурой описываются и запись, и удаление, и перенос
/// (он раскладывается на две правки).
/// </summary>
public readonly struct AnimKeyEdit
{
	public Guid ShapeId { get; init; }
	public string ParamId { get; init; }
	public string Title { get; init; }
	public int Frame { get; init; }
	public AnimValue? Before { get; init; }
	public AnimValue? After { get; init; }
}

/// <summary>
/// Шаг истории для ключей анимации.
///
/// В историю попадают только создание, удаление и перенос ключей.
/// Пуск и остановка воспроизведения — нет: это не правка документа,
/// а просмотр, и секунда проигрывания вытеснила бы из истории
/// настоящую работу пользователя.
///
/// После отката значения кадра раскладываются по фигурам заново:
/// иначе фигуры остались бы в положении, которого в восстановленной
/// анимации уже нет.
/// </summary>
public sealed class AnimKeysAction : UndoableActionBase
{
	private readonly PageAnimation _anim;
	private readonly Page? _page;
	private readonly List<AnimKeyEdit> _edits;
	private readonly string _name;

	public AnimKeysAction(PageAnimation anim, Page? page, string name, IEnumerable<AnimKeyEdit> edits)
	{
		_anim = anim;
		_page = page;
		_name = name;
		_edits = edits.ToList();
	}

	public bool IsEmpty => _edits.Count == 0;

	public override string Name => _name;

	public override long MemoryFootprint => 128 + _edits.Count * 96;

	public override void Undo()
	{
		foreach (var e in _edits)
			_anim.ApplyEdit(e.ShapeId, e.ParamId, e.Title, e.Frame, e.Before);

		Reapply();
	}

	public override void Redo()
	{
		foreach (var e in _edits)
			_anim.ApplyEdit(e.ShapeId, e.ParamId, e.Title, e.Frame, e.After);

		Reapply();
	}

	private void Reapply() => _anim.ApplyFrame(_page, _anim.CurrentFrame);
}
