using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

using VDraw.Animation;
using VDraw.Documents;
using VDraw.Editing;
using VDraw.Editing.Images;
using VDraw.History;
using VDraw.Ui;

namespace VDraw.Shapes;

[Serializable]
public enum CurveNodeType
{
	Straight,
	Sharp,
	Tangent
}

[Serializable]
public sealed class CurveNode
{
	/// <summary>
	/// Устойчивый идентификатор узла.
	///
	/// Привязки ссылаются именно на него, а не на порядковый номер:
	/// номер сдвигается при вставке или удалении соседа, и размер
	/// перескакивал на чужой узел.
	///
	/// Сеттер открыт ради Clone и загрузки документа: снимок узлов
	/// в истории отмены обязан вернуть ТЕ ЖЕ узлы, а не похожие, иначе
	/// откат рвал бы все привязки к ним.
	/// </summary>
	public Guid Id { get; set; } = Guid.NewGuid();

	public SKPoint Position { get; set; }

	public CurveNodeType Type { get; set; } = CurveNodeType.Straight;

	public bool Selected { get; set; }
	public bool Focused { get; set; }

	public SKPoint ControlPrev { get; set; } = new SKPoint(float.NaN, float.NaN);
	public SKPoint ControlNext { get; set; } = new SKPoint(float.NaN, float.NaN);

	public bool BreakAfter = false;
	public bool NewContour = false;
	public bool CloseAfter = false;

	public bool HasPrevControl =>
		Type != CurveNodeType.Straight &&
		!float.IsNaN(ControlPrev.X) &&
		!float.IsNaN(ControlPrev.Y);

	public bool HasNextControl =>
		Type != CurveNodeType.Straight &&
		!float.IsNaN(ControlNext.X) &&
		!float.IsNaN(ControlNext.Y);

	public void ClearControls()
	{
		ControlPrev = new SKPoint(float.NaN, float.NaN);
		ControlNext = new SKPoint(float.NaN, float.NaN);
	}

	public void SetStraight()
	{
		Type = CurveNodeType.Straight;
		ClearControls();
	}

	public void SetSharp(float handleLength = 8f)
	{
		if (!HasPrevControl)
			ControlPrev = new SKPoint(Position.X - handleLength, Position.Y);

		if (!HasNextControl)
			ControlNext = new SKPoint(Position.X + handleLength, Position.Y);

		Type = CurveNodeType.Sharp;
	}

	public void SetTangent(float handleLength = 8f)
	{
		if (!HasPrevControl && !HasNextControl)
		{
			ControlPrev = new SKPoint(Position.X - handleLength, Position.Y);
			ControlNext = new SKPoint(Position.X + handleLength, Position.Y);
		}
		else if (HasNextControl)
		{
			var v = new SKPoint(ControlNext.X - Position.X, ControlNext.Y - Position.Y);
			float len = MathF.Sqrt(v.X * v.X + v.Y * v.Y);
			if (len < 0.0001f)
				len = handleLength;

			float ux = v.X / len;
			float uy = v.Y / len;

			float prevLen = HasPrevControl
				? Distance(Position, ControlPrev)
				: handleLength;

			ControlPrev = new SKPoint(
				Position.X - ux * prevLen,
				Position.Y - uy * prevLen);
		}
		else if (HasPrevControl)
		{
			var v = new SKPoint(ControlPrev.X - Position.X, ControlPrev.Y - Position.Y);
			float len = MathF.Sqrt(v.X * v.X + v.Y * v.Y);
			if (len < 0.0001f)
				len = handleLength;

			float ux = v.X / len;
			float uy = v.Y / len;

			float nextLen = HasNextControl
				? Distance(Position, ControlNext)
				: handleLength;

			ControlNext = new SKPoint(
				Position.X - ux * nextLen,
				Position.Y - uy * nextLen);
		}

		Type = CurveNodeType.Tangent;
	}

	public CurveNode Clone()
	{
		return new CurveNode
		{
			// Идентичность сохраняется: копия узла — это тот же узел
			// в другой момент времени. Именно так работает снимок
			// CurveNodesAction: после отмены привязки обязаны найти свои узлы.
			Id = Id,
			Position = Position,
			Type = Type,
			Selected = Selected,
			Focused = Focused,
			ControlPrev = ControlPrev,
			ControlNext = ControlNext,
			BreakAfter = BreakAfter,
			NewContour = NewContour,
			CloseAfter = CloseAfter
		};
	}

	private static float Distance(SKPoint a, SKPoint b)
	{
		float dx = a.X - b.X;
		float dy = a.Y - b.Y;
		return MathF.Sqrt(dx * dx + dy * dy);
	}

	public override string ToString()
	{
		return $"Node: {Position.X},{Position.Y}";
	}
}

[Serializable]
public partial class CurveShape : Shape
{
	/// <summary>Один контур: последовательность узлов плюс признак замкнутости.</summary>
	private sealed class Contour
	{
		public readonly List<CurveNode> Nodes = new();
		public bool Closed;

		public int Count => Nodes.Count;
		public CurveNode First => Nodes[0];
		public CurveNode Last => Nodes[^1];
	}

	internal readonly struct ContourRange
	{
		public readonly int Start;
		public readonly int End;
		public readonly bool Closed;

		public ContourRange(int start, int end, bool closed)
		{
			Start = start;
			End = end;
			Closed = closed;
		}
	}

	public List<CurveNode> Nodes { get; } = new();

	public SKPathFillType FillType = SKPathFillType.EvenOdd;

	public CurveShape()
	{
		Name = "Curve";
		Fill.Enabled = false;
	}

	public override SKRect GetLocalBounds()
	{
		if (Nodes.Count == 0)
			return SKRect.Empty;

		var pts = new List<SKPoint>(Nodes.Count * 3);
		foreach (var n in Nodes)
		{
			pts.Add(n.Position);
			if (n.HasPrevControl) pts.Add(n.ControlPrev);
			if (n.HasNextControl) pts.Add(n.ControlNext);
		}

		return RectFromPoints(pts);
	}

	public override SKPath BuildLocalPath()
	{
		// Концы подтягиваются к точкам привязки. Дёшево: при отсутствии привязок
		// метод сразу выходит.
		if (HasAnchors)
			ApplyAnchors();

		using var builder = new SKPathBuilder();
		builder.FillType = FillType;

		if (Nodes.Count == 0)
			return builder.Snapshot();

		if (Nodes.Count == 1)
		{
			var p = Nodes[0].Position;
			builder.MoveTo(p.X, p.Y);
			builder.LineTo(p.X, p.Y);
			return builder.Snapshot();
		}

		foreach (var contour in DecomposeContours())
		{
			if (contour.Count == 0)
				continue;

			var start = contour.First;
			builder.MoveTo(start.Position.X, start.Position.Y);

			if (contour.Count == 1)
			{
				// Одиночный узел: рисуем вырожденный сегмент, иначе контур
				// не попадёт в путь и узел станет невидимым.
				builder.LineTo(start.Position.X, start.Position.Y);
				continue;
			}

			for (int i = 0; i < contour.Count - 1; i++)
				AppendSegment(builder, contour.Nodes[i], contour.Nodes[i + 1]);

			if (contour.Closed)
			{
				// Замыкающий сегмент строится явно: у него могут быть свои
				// контрольные точки, и Close() их не воспроизведёт.
				AppendSegment(builder, contour.Last, start);
				builder.Close();
			}
		}

		return builder.Snapshot();
	}

	public override CurveShape? ToCurveShape()
	{
		var clone = new CurveShape
		{
			Name = Name,
			Layer = Layer,
			Stroke = Stroke,
			Fill = Fill,
			IsVisible = IsVisible,
			IsLocked = IsLocked,
			IsFocused = IsFocused,
			LocalTransform = LocalTransform
		};

		foreach (var n in Nodes)
			clone.Nodes.Add(n.Clone());

		return clone;
	}

	public IEnumerable<CurveNode> SelectedNodes => Nodes.Where(n => n.Selected);

	public CurveNode? FocusedNode => Nodes.FirstOrDefault(n => n.Focused);

	public void ClearNodeSelection()
	{
		foreach (var n in Nodes)
		{
			n.Selected = false;
			n.Focused = false;
		}
	}

	#region Редактирование узлов

	public bool DeleteSelectedNodes(UndoStack? undo = null)
	{
		if (Nodes.Count == 0 || !Nodes.Any(n => n.Selected))
			return false;

		var history = CurveNodesAction.Capture("Delete nodes", this);

		var contours = DecomposeContours();

		foreach (var contour in contours)
			contour.Nodes.RemoveAll(n => n.Selected);

		// Контуры, опустевшие полностью, исчезают.
		contours.RemoveAll(c => c.Count == 0);

		// Из одного узла замкнутый контур не построить.
		foreach (var contour in contours)
		{
			if (contour.Count < 2)
				contour.Closed = false;
		}

		RebuildFromContours(contours);

		return CommitNodeHistory(history, undo);
	}

	public bool MergeSelectedNodes(UndoStack? undo = null)
	{
		int firstIdx = -1;
		int lastIdx = -1;

		for (int i = 0; i < Nodes.Count; i++)
		{
			if (!Nodes[i].Selected)
				continue;

			if (firstIdx < 0)
				firstIdx = i;
			else if (lastIdx < 0)
				lastIdx = i;
			else
				return false;   // выбрано больше двух узлов
		}

		if (firstIdx < 0 || lastIdx < 0)
			return false;

		var contours = DecomposeContours();

		if (!TryLocateNode(contours, firstIdx, out int ciA, out int liA))
			return false;

		if (!TryLocateNode(contours, lastIdx, out int ciB, out int liB))
			return false;

		var history = CurveNodesAction.Capture("Merge nodes", this);

		bool ok = ciA == ciB
			? CloseContour(contours, ciA, liA, liB)
			: JoinContours(contours, ciA, liA, ciB, liB);

		if (!ok)
		{
			history.Discard();
			return false;
		}

		RebuildFromContours(contours);

		return CommitNodeHistory(history, undo);
	}

	/// <summary>Слияние двух концов одного разомкнутого контура — контур замыкается.</summary>
	private bool CloseContour(List<Contour> contours, int contourIndex, int liA, int liB)
	{
		var contour = contours[contourIndex];

		if (contour.Closed)
			return false;

		int lo = Math.Min(liA, liB);
		int hi = Math.Max(liA, liB);

		// Сливать можно только два конца контура.
		if (lo != 0 || hi != contour.Count - 1 || lo == hi)
			return false;

		// Входящая ручка приходит от предпоследнего узла — берём у последнего.
		// Исходящая ведёт ко второму узлу — берём у первого.
		var merged = MakeMergedNode(contour.Last, contour.First);

		var rebuilt = new Contour { Closed = true };
		rebuilt.Nodes.Add(merged);

		for (int k = 1; k < contour.Count - 1; k++)
			rebuilt.Nodes.Add(contour.Nodes[k]);

		// Остался один узел — замыкать нечего.
		if (rebuilt.Count < 2)
			rebuilt.Closed = false;

		contours[contourIndex] = rebuilt;
		return true;
	}

	/// <summary>Соединение концов двух разных разомкнутых контуров в один.</summary>
	private bool JoinContours(List<Contour> contours, int ciA, int liA, int ciB, int liB)
	{
		var a = contours[ciA];
		var b = contours[ciB];

		if (a.Closed || b.Closed)
			return false;

		bool aIsStart = liA == 0;
		bool aIsEnd = liA == a.Count - 1;
		bool bIsStart = liB == 0;
		bool bIsEnd = liB == b.Count - 1;

		if ((!aIsStart && !aIsEnd) || (!bIsStart && !bIsEnd))
			return false;

		// Приводим к виду «хвост A стыкуется с головой B».
		if (aIsStart)
			ReverseContourNodes(a);

		if (bIsEnd)
			ReverseContourNodes(b);

		var merged = MakeMergedNode(a.Last, b.First);

		var joined = new Contour { Closed = false };

		for (int k = 0; k < a.Count - 1; k++)
			joined.Nodes.Add(a.Nodes[k]);

		joined.Nodes.Add(merged);

		for (int k = 1; k < b.Count; k++)
			joined.Nodes.Add(b.Nodes[k]);

		// Сначала ставим объединённый контур на место A, потом убираем B:
		// обратный порядок сдвинул бы индекс A, если B был раньше в списке.
		contours[ciA] = joined;
		contours.Remove(b);

		return true;
	}

	public bool SplitNode(int index, UndoStack? undo = null)
	{
		if (index < 0 || index >= Nodes.Count || Nodes.Count < 2)
			return false;

		var contours = DecomposeContours();

		if (!TryLocateNode(contours, index, out int ci, out int li))
			return false;

		var contour = contours[ci];

		var history = CurveNodesAction.Capture("Split node", this);

		if (contour.Closed)
		{
			if (contour.Count < 2)
			{
				history.Discard();
				return false;
			}

			// Прокручиваем список так, чтобы разрезаемый узел стал первым,
			// и дублируем его в конце — он же становится вторым концом.
			var rotated = new Contour { Closed = false };

			for (int k = 0; k < contour.Count; k++)
				rotated.Nodes.Add(contour.Nodes[(li + k) % contour.Count]);

			var tailCopy = contour.Nodes[li].Clone();
			tailCopy.Selected = false;
			tailCopy.Focused = false;
			rotated.Nodes.Add(tailCopy);

			// У первого узла нет входящего сегмента, у последнего — исходящего.
			rotated.First.ControlPrev = new SKPoint(float.NaN, float.NaN);
			rotated.Last.ControlNext = new SKPoint(float.NaN, float.NaN);

			contours[ci] = rotated;
		}
		else
		{
			// Концы разомкнутого контура делить нечего.
			if (li == 0 || li == contour.Count - 1)
			{
				history.Discard();
				return false;
			}

			var left = new Contour { Closed = false };
			for (int k = 0; k <= li; k++)
				left.Nodes.Add(contour.Nodes[k]);

			var right = new Contour { Closed = false };

			var headCopy = contour.Nodes[li].Clone();
			headCopy.Selected = false;
			headCopy.Focused = false;
			right.Nodes.Add(headCopy);

			for (int k = li + 1; k < contour.Count; k++)
				right.Nodes.Add(contour.Nodes[k]);

			left.Last.ControlNext = new SKPoint(float.NaN, float.NaN);
			right.First.ControlPrev = new SKPoint(float.NaN, float.NaN);

			contours[ci] = left;
			contours.Insert(ci + 1, right);
		}

		RebuildFromContours(contours);

		return CommitNodeHistory(history, undo);
	}
	#endregion

	#region Вспомогательные методы слияния

	private bool CommitNodeHistory(CurveNodesAction history, UndoStack? undo)
	{
		history.CaptureAfter();

		if (!history.HasChanges())
		{
			history.Discard();
			return false;
		}

		undo?.Push(history);
		return true;
	}

	private static int FindContourIndex(List<ContourRange> ranges, int nodeIndex)
	{
		for (int i = 0; i < ranges.Count; i++)
		{
			if (ranges[i].Start <= nodeIndex && nodeIndex <= ranges[i].End)
				return i;
		}

		return -1;
	}

	/// <summary>Слияние двух концов одного разомкнутого контура — контур замыкается.</summary>
	private bool CloseContourByMerge(ContourRange range, int firstIdx, int lastIdx)
	{
		if (range.Closed)
			return false;

		if (firstIdx != range.Start || lastIdx != range.End || firstIdx == lastIdx)
			return false;

		var first = Nodes[firstIdx];
		var last = Nodes[lastIdx];

		var avg = new SKPoint(
			(first.Position.X + last.Position.X) * 0.5f,
			(first.Position.Y + last.Position.Y) * 0.5f);

		var merged = new CurveNode
		{
			Position = avg,
			Type = CurveNodeType.Tangent,
			Selected = true,
			Focused = true,

			// Входящая ручка приходит от предпоследнего узла — берём у last.
			// Исходящая ведёт ко второму узлу — берём у first.
			// Обе смещаются вместе с узлом в новую позицию.
			ControlPrev = last.HasPrevControl
				? Offset(last.ControlPrev, avg, last.Position)
				: avg,

			ControlNext = first.HasNextControl
				? Offset(first.ControlNext, avg, first.Position)
				: avg,

			NewContour = true,
			BreakAfter = false,
			CloseAfter = false
		};

		bool hasInteriorNodes = lastIdx - firstIdx > 1;

		Nodes.RemoveAt(lastIdx);
		Nodes.RemoveAt(firstIdx);
		Nodes.Insert(firstIdx, merged);

		if (hasInteriorNodes)
		{
			// Узел, шедший непосредственно перед lastIdx, после двух удалений
			// и одной вставки оказывается ровно на индексе lastIdx - 1.
			int lastInteriorIdx = lastIdx - 1;

			if (lastInteriorIdx > firstIdx && lastInteriorIdx < Nodes.Count)
			{
				Nodes[lastInteriorIdx].CloseAfter = true;
				Nodes[lastInteriorIdx].BreakAfter = false;
			}
		}
		else
		{
			// От контура остался один узел — замыкать нечего.
			merged.CloseAfter = false;
		}

		NormalizeCurveTopology();
		return true;
	}

	/// <summary>Соединение концов двух разных разомкнутых контуров в один.</summary>
	private bool JoinTwoContours(ContourRange rangeA, int nodeA, ContourRange rangeB, int nodeB)
	{
		if (rangeA.Closed || rangeB.Closed)
			return false;

		bool aIsStart = nodeA == rangeA.Start;
		bool aIsEnd = nodeA == rangeA.End;
		bool bIsStart = nodeB == rangeB.Start;
		bool bIsEnd = nodeB == rangeB.End;

		// Сливать можно только концевые узлы.
		if ((!aIsStart && !aIsEnd) || (!bIsStart && !bIsEnd))
			return false;

		var listA = ExtractContour(rangeA);
		var listB = ExtractContour(rangeB);

		// Приводим к виду «хвост A стыкуется с головой B».
		if (aIsStart)
			ReverseContour(listA);

		if (bIsEnd)
			ReverseContour(listB);

		var tail = listA[^1];
		var head = listB[0];

		var avg = new SKPoint(
			(tail.Position.X + head.Position.X) * 0.5f,
			(tail.Position.Y + head.Position.Y) * 0.5f);

		var merged = new CurveNode
		{
			Position = avg,
			Type = CurveNodeType.Tangent,
			Selected = true,
			Focused = true,

			ControlPrev = tail.HasPrevControl
				? Offset(tail.ControlPrev, avg, tail.Position)
				: avg,

			ControlNext = head.HasNextControl
				? Offset(head.ControlNext, avg, head.Position)
				: avg,

			NewContour = false,
			BreakAfter = false,
			CloseAfter = false
		};

		listA[^1] = merged;
		listA.AddRange(listB.GetRange(1, listB.Count - 1));

		listA[0].NewContour = true;
		listA[^1].CloseAfter = false;
		listA[^1].BreakAfter = true;

		// Удаляем оба исходных контура, начиная с того, что дальше по списку,
		// иначе индексы второго диапазона сдвинутся.
		var firstRange = rangeA.Start < rangeB.Start ? rangeA : rangeB;
		var secondRange = rangeA.Start < rangeB.Start ? rangeB : rangeA;

		Nodes.RemoveRange(secondRange.Start, secondRange.End - secondRange.Start + 1);
		Nodes.RemoveRange(firstRange.Start, firstRange.End - firstRange.Start + 1);

		Nodes.InsertRange(firstRange.Start, listA);

		NormalizeCurveTopology();
		return true;
	}

	private List<CurveNode> ExtractContour(ContourRange range)
	{
		var result = new List<CurveNode>(range.End - range.Start + 1);

		for (int i = range.Start; i <= range.End; i++)
			result.Add(Nodes[i]);

		return result;
	}

	/// <summary>
	/// Разворот контура: порядок узлов инвертируется, входящие и исходящие
	/// ручки меняются местами — иначе кривизна сегментов вывернется наизнанку.
	/// </summary>
	private static void ReverseContour(List<CurveNode> nodes)
	{
		nodes.Reverse();

		foreach (var n in nodes)
		{
			(n.ControlPrev, n.ControlNext) = (n.ControlNext, n.ControlPrev);
			n.NewContour = false;
			n.BreakAfter = false;
			n.CloseAfter = false;
		}
	}

	/// <summary>Переносит контрольную точку вместе с узлом из oldPos в newPos.</summary>
	private static SKPoint Offset(SKPoint control, SKPoint newPos, SKPoint oldPos)
		=> new SKPoint(
			control.X + (newPos.X - oldPos.X),
			control.Y + (newPos.Y - oldPos.Y));

	#endregion

	private void NormalizeCurveTopology()
	{
		if (Nodes.Count == 0)
			return;

		if (Nodes.Count == 1)
		{
			Nodes[0].NewContour = true;
			Nodes[0].BreakAfter = true;
			Nodes[0].CloseAfter = false;

			if (Nodes[0].Type == CurveNodeType.Straight)
				Nodes[0].ClearControls();

			return;
		}

		RebuildFromContours(DecomposeContours());
	}


	public int FindFocusedOrSelectedNodeIndex()
	{
		int idx = Nodes.FindIndex(n => n.Focused);
		if (idx >= 0)
			return idx;

		return Nodes.FindIndex(n => n.Selected);
	}

	/// <summary>
	/// Меняет тип выделенных узлов.
	///
	/// Это изменение геометрии, а не оформления: вместе с типом
	/// пересчитываются контрольные точки, и переход Tangent → Straight
	/// теряет их безвозвратно. Без шага истории вернуть форму было бы нечем.
	/// </summary>
	public bool SetSelectedNodeType(CurveNodeType type, float handleLength = 8f, UndoStack? undo = null)
	{
		var history = CurveNodesAction.Capture("Node type", this);

		ApplySelectedNodeType(type, handleLength);

		return CommitNodeHistory(history, undo);
	}

	private void ApplySelectedNodeType(CurveNodeType type, float handleLength)
	{
		for (int i = 0; i < Nodes.Count; i++)
		{
			var n = Nodes[i];
			if (!n.Selected)
				continue;

			switch (type)
			{
				case CurveNodeType.Straight:
					n.SetStraight();
					break;

				case CurveNodeType.Sharp:
					n.SetSharp(handleLength);
					break;

				case CurveNodeType.Tangent:
					SetNodeTangentWithGeometry(i); // вместо n.SetTangent(handleLength);
					break;
			}
		}
	}

	private void SetNodeTangentWithGeometry(int index, float figureHandleScale = 0.05f)
	{
		if (index < 0 || index >= Nodes.Count)
			return;

		var node = Nodes[index];

		CurveNode? prevNode = index > 0 ? Nodes[index - 1] : null;
		CurveNode? nextNode = index < Nodes.Count - 1 ? Nodes[index + 1] : null;

		if (prevNode == null || nextNode == null)
		{
			node.SetTangent();
			return;
		}

		var vIn = new SKPoint(
			node.Position.X - prevNode.Position.X,
			node.Position.Y - prevNode.Position.Y);

		var vOut = new SKPoint(
			nextNode.Position.X - node.Position.X,
			nextNode.Position.Y - node.Position.Y);

		float lenIn = MathF.Sqrt(vIn.X * vIn.X + vIn.Y * vIn.Y);
		float lenOut = MathF.Sqrt(vOut.X * vOut.X + vOut.Y * vOut.Y);

		if (lenIn < 0.0001f || lenOut < 0.0001f)
		{
			node.SetTangent();
			return;
		}

		var uIn = new SKPoint(vIn.X / lenIn, vIn.Y / lenIn);
		var uOut = new SKPoint(vOut.X / lenOut, vOut.Y / lenOut);

		var bisector = new SKPoint(
			uIn.X + uOut.X,
			uIn.Y + uOut.Y);

		float lenBis = MathF.Sqrt(bisector.X * bisector.X + bisector.Y * bisector.Y);
		if (lenBis < 0.0001f)
		{
			bisector = uOut;
			lenBis = 1f;
		}

		var uBis = new SKPoint(bisector.X / lenBis, bisector.Y / lenBis);

		// Перпендикуляр к биссектору (из него будем вращать на 90°)
		var n = new SKPoint(-uBis.Y, uBis.X);

		var bounds = GetLocalBounds();
		float figSize = MathF.Max(bounds.Width, bounds.Height);
		float handleLen = figSize * figureHandleScale;

		// Базовые ручки по перпендикуляру
		var basePrev = new SKPoint(
			node.Position.X - n.X * handleLen,
			node.Position.Y - n.Y * handleLen);

		var baseNext = new SKPoint(
			node.Position.X + n.X * handleLen,
			node.Position.Y + n.Y * handleLen);

		// Поворот на -90° относительно узла
		SKPoint RotateMinus90(SKPoint p)
		{
			float dx = p.X - node.Position.X;
			float dy = p.Y - node.Position.Y;

			// Поворот на -90° вокруг узла:
			// x' = node.X + dy
			// y' = node.Y - dx
			return new SKPoint(
				node.Position.X + dy,
				node.Position.Y - dx);
		}

		node.ControlPrev = RotateMinus90(basePrev);
		node.ControlNext = RotateMinus90(baseNext);
		node.Type = CurveNodeType.Tangent;
	}

	public void MoveNode(int index, SKPoint newLocalPoint)
	{
		if (index < 0 || index >= Nodes.Count)
			return;

		var node = Nodes[index];
		var delta = new SKPoint(
			newLocalPoint.X - node.Position.X,
			newLocalPoint.Y - node.Position.Y);

		node.Position = newLocalPoint;

		if (node.HasPrevControl)
		{
			node.ControlPrev = new SKPoint(
				node.ControlPrev.X + delta.X,
				node.ControlPrev.Y + delta.Y);
		}

		if (node.HasNextControl)
		{
			node.ControlNext = new SKPoint(
				node.ControlNext.X + delta.X,
				node.ControlNext.Y + delta.Y);
		}
	}

	public void MoveControlPoint(int nodeIndex, bool isPrev, SKPoint newLocalPoint)
	{
		if (nodeIndex < 0 || nodeIndex >= Nodes.Count)
			return;

		var node = Nodes[nodeIndex];

		if (node.Type == CurveNodeType.Straight)
			node.Type = CurveNodeType.Sharp;

		if (isPrev)
			node.ControlPrev = newLocalPoint;
		else
			node.ControlNext = newLocalPoint;

		if (node.Type == CurveNodeType.Tangent)
		{
			var moved = isPrev ? node.ControlPrev : node.ControlNext;
			float vx = moved.X - node.Position.X;
			float vy = moved.Y - node.Position.Y;
			float len = MathF.Sqrt(vx * vx + vy * vy);

			if (len < 0.0001f)
				return;

			float ux = vx / len;
			float uy = vy / len;

			if (isPrev)
			{
				float otherLen = node.HasNextControl
					? Distance(node.Position, node.ControlNext)
					: len;

				node.ControlNext = new SKPoint(
					node.Position.X - ux * otherLen,
					node.Position.Y - uy * otherLen);
			}
			else
			{
				float otherLen = node.HasPrevControl
					? Distance(node.Position, node.ControlPrev)
					: len;

				node.ControlPrev = new SKPoint(
					node.Position.X - ux * otherLen,
					node.Position.Y - uy * otherLen);
			}
		}
	}

	public bool InsertNodeOnSegment(int segmentIndex, SKPoint localPoint)
	{
		if (Nodes.Count < 2)
			return false;

		var contours = DecomposeContours();

		if (!TryLocateNode(contours, segmentIndex, out int ci, out int li))
			return false;

		var contour = contours[ci];

		// Сегмент существует, если за узлом есть следующий либо контур замкнут.
		bool isClosingSegment = li == contour.Count - 1;

		if (isClosingSegment && !contour.Closed)
			return false;

		ClearNodeSelection();

		var node = new CurveNode
		{
			Position = localPoint,
			Type = CurveNodeType.Straight,
			Selected = true,
			Focused = true
		};

		// Для замыкающего сегмента новый узел становится последним в контуре,
		// для обычного — встаёт сразу после узла li.
		contour.Nodes.Insert(li + 1, node);

		RebuildFromContours(contours);
		return true;
	}

	private static void AppendSegment(SKPathBuilder builder, CurveNode a, CurveNode b)
	{
		bool useCubic =
			a.HasNextControl ||
			b.HasPrevControl ||
			a.Type != CurveNodeType.Straight ||
			b.Type != CurveNodeType.Straight;

		if (!useCubic)
		{
			builder.LineTo(b.Position.X, b.Position.Y);
			return;
		}

		var c1 = a.HasNextControl ? a.ControlNext : a.Position;
		var c2 = b.HasPrevControl ? b.ControlPrev : b.Position;

		builder.CubicTo(
			c1.X, c1.Y,
			c2.X, c2.Y,
			b.Position.X, b.Position.Y);
	}

	internal List<ContourRange> GetContourRanges()
	{
		var result = new List<ContourRange>();
		int index = 0;

		foreach (var contour in DecomposeContours())
		{
			if (contour.Count == 0)
				continue;

			result.Add(new ContourRange(index, index + contour.Count - 1, contour.Closed));
			index += contour.Count;
		}

		return result;
	}

	public override int GetGeometryHash()
	{
		var hash = new HashCode();

		hash.Add(Nodes.Count);
		hash.Add((int)FillType);

		foreach (var node in Nodes)
		{
			hash.Add(node.Position.X);
			hash.Add(node.Position.Y);

			// Контрольные точки могут быть NaN у прямых узлов — это стабильное
			// значение хеша, отдельная обработка не нужна.
			hash.Add(node.ControlPrev.X);
			hash.Add(node.ControlPrev.Y);
			hash.Add(node.ControlNext.X);
			hash.Add(node.ControlNext.Y);

			hash.Add((int)node.Type);

			// Флаги топологии меняют форму пути, не трогая координаты:
			// снятие CloseAfter размыкает контур, и область становится другой.
			hash.Add(node.NewContour);
			hash.Add(node.BreakAfter);
			hash.Add(node.CloseAfter);
		}

		var m = LocalTransform;

		hash.Add(m.ScaleX);
		hash.Add(m.SkewX);
		hash.Add(m.TransX);
		hash.Add(m.SkewY);
		hash.Add(m.ScaleY);
		hash.Add(m.TransY);

		return hash.ToHashCode();
	}

	/// <summary>
	/// Путь обводки, укороченный с концов под стрелочные глифы.
	///
	/// Укорачиваются только РАЗОМКНУТЫЕ контуры и ровно те же, на концах
	/// которых DrawStrokeGlyphs ставит глифы, — иначе линия и наконечник
	/// разошлись бы.
	///
	/// Отступ ограничен сорока пятью процентами длины контура: у совсем
	/// короткого сегмента два отступа съели бы его целиком, и линия
	/// пропала бы вовсе.
	/// </summary>
	protected override SKPath? BuildStrokeWorldPath(SKPath worldPath, float mmPerWorldUnit)
	{
		if (worldPath == null || worldPath.IsEmpty)
			return null;

		float insetStart = LineGlyphLibrary.GetLineInsetWorld(
			Stroke.StartGlyph,
			Stroke.GetStartGlyphSizeWorld(mmPerWorldUnit));

		float insetEnd = LineGlyphLibrary.GetLineInsetWorld(
			Stroke.EndGlyph,
			Stroke.GetEndGlyphSizeWorld(mmPerWorldUnit));

		if (insetStart <= 0f && insetEnd <= 0f)
			return null;

		var result = new SKPath { FillType = worldPath.FillType };

		using var measure = new SKPathMeasure(worldPath, false);

		do
		{
			float len = measure.Length;

			if (len <= 0.0001f)
				continue;

			using var segment = new SKPath();

			// Замкнутый контур глифов не получает и укорачиванию не подлежит.
			if (measure.IsClosed)
			{
				if (measure.GetSegment(0f, len, segment, true))
					result.AddPath(segment);

				continue;
			}

			float maxInset = len * 0.45f;

			float a = MathF.Min(insetStart, maxInset);
			float b = MathF.Min(insetEnd, maxInset);

			if (measure.GetSegment(a, len - b, segment, true))
				result.AddPath(segment);
		}
		while (measure.NextContour());

		if (result.IsEmpty)
		{
			result.Dispose();
			return null;
		}

		return result;
	}

	protected override void DrawStrokeGlyphs(SKCanvas canvas, float mmPerWorldUnit, SKPath worldPath, SKPaint strokePaint)
	{
		if (string.IsNullOrEmpty(Stroke.StartGlyph) && string.IsNullOrEmpty(Stroke.EndGlyph))
			return;

		if (Nodes.Count < 2)
			return;

		var ranges = GetContourRanges();
		if (ranges.Count == 0)
			return;

		var worldM = GetWorldTransform();

		float startSizeWorld = Stroke.GetStartGlyphSizeWorld(mmPerWorldUnit);
		float endSizeWorld = Stroke.GetEndGlyphSizeWorld(mmPerWorldUnit);

		foreach (var r in ranges)
		{
			// Глифы только для разомкнутых контуров.
			if (r.Closed)
				continue;

			int s = r.Start;
			int e = r.End;

			if (e <= s || s < 0 || e >= Nodes.Count)
				continue;

			var pStart = worldM.MapPoint(Nodes[s].Position);
			var pEnd = worldM.MapPoint(Nodes[e].Position);

			var dirStart = GetStartTangentWorld(worldM, s);
			var dirEnd = GetEndTangentWorld(worldM, e);

			// Направление у LineGlyphLibrary.Draw — это КУДА СМОТРИТ глиф,
			// а не куда идёт линия. На конце контура эти две вещи совпадают,
			// а в начале противоположны: касательная там направлена внутрь
			// контура, а наконечник обязан смотреть наружу. Отсюда разворот
			// только для начального глифа.
			if (!string.IsNullOrEmpty(Stroke.StartGlyph))
			{
				var outward = new SKPoint(-dirStart.X, -dirStart.Y);

				DrawLineGlyph(canvas, pStart, outward, startSizeWorld, Stroke.StartGlyph, strokePaint);
			}

			if (!string.IsNullOrEmpty(Stroke.EndGlyph))
				DrawLineGlyph(canvas, pEnd, dirEnd, endSizeWorld, Stroke.EndGlyph, strokePaint);
		}
	}

	/// <summary>
	/// Касательная к сегменту в его начальном узле, в мировых координатах.
	///
	/// Прежний код брал направление на соседний УЗЕЛ и игнорировал
	/// контрольные точки. Для прямого сегмента это одно и то же, а для
	/// сегмента Безье — нет: кривая выходит из узла по касательной к
	/// ControlNext, и глиф оказывался повёрнут на произвольный угол.
	///
	/// Порядок предпочтений: собственная контрольная точка узла, затем
	/// входная контрольная точка соседа, затем сам сосед. Вырожденные
	/// (совпадающие с узлом) контрольные точки пропускаются: направление
	/// по ним не восстановить.
	/// </summary>
	private SKPoint GetStartTangentWorld(SKMatrix worldM, int startIndex)
	{
		var node = Nodes[startIndex];
		var next = Nodes[startIndex + 1];

		if (node.HasNextControl && TryDirection(worldM, node.Position, node.ControlNext, out var d))
			return d;

		if (next.HasPrevControl && TryDirection(worldM, node.Position, next.ControlPrev, out d))
			return d;

		TryDirection(worldM, node.Position, next.Position, out d);
		return d;
	}

	/// <summary>
	/// Касательная к сегменту в его конечном узле, в мировых координатах.
	/// Зеркальна <see cref="GetStartTangentWorld"/>: кривая входит в узел
	/// по касательной к его ControlPrev.
	/// </summary>
	private SKPoint GetEndTangentWorld(SKMatrix worldM, int endIndex)
	{
		var node = Nodes[endIndex];
		var prev = Nodes[endIndex - 1];

		if (node.HasPrevControl && TryDirection(worldM, node.ControlPrev, node.Position, out var d))
			return d;

		if (prev.HasNextControl && TryDirection(worldM, prev.ControlNext, node.Position, out d))
			return d;

		TryDirection(worldM, prev.Position, node.Position, out d);
		return d;
	}

	/// <summary>
	/// Вектор from-to в мировых координатах. Обе точки переводятся
	/// трансформацией по отдельности: вычитание до перевода потеряло бы
	/// поворот и масштаб.
	/// </summary>
	private static bool TryDirection(SKMatrix worldM, SKPoint fromLocal, SKPoint toLocal, out SKPoint direction)
	{
		var a = worldM.MapPoint(fromLocal);
		var b = worldM.MapPoint(toLocal);

		direction = new SKPoint(b.X - a.X, b.Y - a.Y);

		return MathF.Abs(direction.X) > 0.0001f || MathF.Abs(direction.Y) > 0.0001f;
	}

	private static float Distance(SKPoint a, SKPoint b)
	{
		float dx = a.X - b.X;
		float dy = a.Y - b.Y;
		return MathF.Sqrt(dx * dx + dy * dy);
	}

	public override Shape Clone()
	{
		var clone = CreateCloneInstance();

		clone.Name = Name;
		clone.Layer = Layer;
		clone.Stroke = Stroke.Clone();
		clone.Fill = Fill.Clone();
		clone.IsVisible = IsVisible;
		clone.IsLocked = IsLocked;
		clone.IsFocused = false;
		clone.LocalTransform = LocalTransform;
		clone.FillType = FillType;
		clone.AnchorStubFactor = AnchorStubFactor;

		foreach (var n in Nodes)
			clone.Nodes.Add(n.Clone());

		// Привязки копируются содержимым, а не ссылкой: иначе копия и оригинал
		// делили бы одну привязку, и снятие связи у одного снимало бы её у другого.
		clone.FirstAnchor.CopyFrom(FirstAnchor);
		clone.LastAnchor.CopyFrom(LastAnchor);
		clone.InvalidateAnchors();

		return clone;
	}

	/// <summary>
	/// Создаёт пустой экземпляр нужного типа для клонирования.
	///
	/// Наследники обязаны переопределить, иначе Clone вернёт CurveShape вместо
	/// их типа, и приведение у вызывающего упадёт. Копирование общих полей
	/// остаётся в одном месте — в Clone базового класса.
	/// </summary>
	protected virtual CurveShape CreateCloneInstance() => new CurveShape();

	#region Разбор и сборка контуров

	/// <summary>
	/// Разбирает плоский список узлов на контуры. Единственное место, где
	/// читаются флаги NewContour / BreakAfter / CloseAfter.
	/// </summary>
	private List<Contour> DecomposeContours()
	{
		var result = new List<Contour>();
		Contour? current = null;

		for (int i = 0; i < Nodes.Count; i++)
		{
			var node = Nodes[i];

			// Новый контур начинается на первом узле списка либо там,
			// где стоит NewContour, либо после обрыва (current == null).
			if (current == null || (i > 0 && node.NewContour))
			{
				current = new Contour();
				result.Add(current);
			}

			current.Nodes.Add(node);

			if (node.CloseAfter)
			{
				current.Closed = true;
				current = null;
				continue;
			}

			if (node.BreakAfter)
				current = null;
		}

		// Замкнутый контур меньше чем из двух узлов замкнутым быть не может.
		foreach (var c in result)
		{
			if (c.Count < 2)
				c.Closed = false;
		}

		return result;
	}

	/// <summary>
	/// Собирает плоский список узлов из контуров и расставляет флаги заново.
	/// После этого метода топология гарантированно согласована — отдельная
	/// «починка» флагов больше не нужна.
	/// </summary>
	private void RebuildFromContours(List<Contour> contours)
	{
		Nodes.Clear();

		foreach (var contour in contours)
		{
			if (contour.Count == 0)
				continue;

			// Сбрасываем все флаги, чтобы не унаследовать мусор от предыдущей
			// топологии: именно на этом ломались прежние версии операций.
			foreach (var node in contour.Nodes)
			{
				node.NewContour = false;
				node.BreakAfter = false;
				node.CloseAfter = false;

				if (node.Type == CurveNodeType.Straight)
					node.ClearControls();
			}

			contour.First.NewContour = true;

			if (contour.Closed && contour.Count >= 2)
				contour.Last.CloseAfter = true;
			else
				contour.Last.BreakAfter = true;

			Nodes.AddRange(contour.Nodes);
		}
	}

	/// <summary>Находит контур и позицию узла внутри него по глобальному индексу.</summary>
	private bool TryLocateNode(
		List<Contour> contours,
		int globalIndex,
		out int contourIndex,
		out int localIndex)
	{
		contourIndex = -1;
		localIndex = -1;

		if (globalIndex < 0 || globalIndex >= Nodes.Count)
			return false;

		var target = Nodes[globalIndex];

		for (int c = 0; c < contours.Count; c++)
		{
			int idx = contours[c].Nodes.IndexOf(target);
			if (idx >= 0)
			{
				contourIndex = c;
				localIndex = idx;
				return true;
			}
		}

		return false;
	}

	/// <summary>
	/// Разворот контура: порядок узлов инвертируется, входящие и исходящие
	/// ручки меняются местами — иначе кривизна сегментов вывернется наизнанку.
	/// </summary>
	private static void ReverseContourNodes(Contour contour)
	{
		contour.Nodes.Reverse();

		foreach (var n in contour.Nodes)
			(n.ControlPrev, n.ControlNext) = (n.ControlNext, n.ControlPrev);
	}

	/// <summary>
	/// Объединяет два узла в один. Входящая ручка берётся у того узла, из
	/// которого приходит сегмент, исходящая — у того, в который сегмент уходит.
	/// Обе переносятся вместе с узлом в новую позицию.
	/// </summary>
	private static CurveNode MakeMergedNode(CurveNode incomingFrom, CurveNode outgoingTo)
	{
		var mid = new SKPoint(
			(incomingFrom.Position.X + outgoingTo.Position.X) * 0.5f,
			(incomingFrom.Position.Y + outgoingTo.Position.Y) * 0.5f);

		bool hasPrev = incomingFrom.HasPrevControl;
		bool hasNext = outgoingTo.HasNextControl;

		var merged = new CurveNode
		{
			Position = mid,
			Selected = true,
			Focused = true,
			Type = (hasPrev || hasNext) ? CurveNodeType.Tangent : CurveNodeType.Straight
		};

		merged.ControlPrev = hasPrev
			? Shift(incomingFrom.ControlPrev, incomingFrom.Position, mid)
			: new SKPoint(float.NaN, float.NaN);

		merged.ControlNext = hasNext
			? Shift(outgoingTo.ControlNext, outgoingTo.Position, mid)
			: new SKPoint(float.NaN, float.NaN);

		return merged;
	}

	/// <summary>Переносит контрольную точку вместе с узлом из oldPos в newPos.</summary>
	private static SKPoint Shift(SKPoint control, SKPoint oldPos, SKPoint newPos)
		=> new SKPoint(
			control.X + (newPos.X - oldPos.X),
			control.Y + (newPos.Y - oldPos.Y));

	#endregion

	/// <summary>Диапазоны контуров для адорнеров и хит-теста.</summary>
	internal IReadOnlyList<(int Start, int End, bool Closed)> GetContourRangesForAdorner()
	{
		var list = new List<(int, int, bool)>();
		int index = 0;
		foreach (var contour in DecomposeContours())
		{
			if (contour.Count == 0)
				continue;

			list.Add((index, index + contour.Count - 1, contour.Closed));
			index += contour.Count;
		}

		return list;
	}
}

/// <summary>
/// Привязка концов кривой к другим фигурам — основа связей во flowchart.
///
/// Отдельный файл частичного класса. В Curves.cs нужны три правки, см.
/// LinkAnchorsInstall.md.
/// </summary>
public partial class CurveShape : IShapeAnchorHost
{
	#region Привязки

	/// <summary>Привязка самого первого узла кривой.</summary>
	public ShapeAnchor FirstAnchor { get; private set; } = new();

	/// <summary>Привязка самого последнего узла кривой.</summary>
	public ShapeAnchor LastAnchor { get; private set; } = new();

	/// <summary>Есть ли хотя бы одна активная привязка.</summary>
	public bool HasAnchors => FirstAnchor.IsActive || LastAnchor.IsActive;

	/// <summary>
	/// Доля расстояния до соседнего узла, уходящая в контрольную точку выхода.
	///
	/// Пропорция, а не миллиметры: у короткой связи короткий выступ, у длинной
	/// плавная дуга. Так ведут себя коннекторы в Visio и draw.io. Фиксированная
	/// длина на коротких связях давала бы петлю.
	/// </summary>
	public float AnchorStubFactor { get; set; } = 0.35f;

	private int _anchorsAppliedVersion = -1;

	/// <summary>Пересчитать привязки при следующем построении пути.</summary>
	public void InvalidateAnchors() => _anchorsAppliedVersion = -1;

	/// <summary>
	/// Подтягивает крайние узлы к точкам привязки и задаёт направление выхода.
	///
	/// Вызывается перед построением пути. Именно поэтому связь переживает любое
	/// преобразование обеих фигур: положение узла не хранится, а вычисляется
	/// заново из текущей геометрии цели.
	///
	/// Возвращает true, если что-то изменилось.
	/// </summary>
	public bool ApplyAnchors()
	{
		if (Nodes.Count == 0)
			return false;

		int signature = ComputeAnchorSignature();

		// Ничего не изменилось с прошлого пересчёта — узлы уже на местах.
		if (signature == _anchorsAppliedVersion)
			return false;

		bool changed = false;

		if (FirstAnchor.IsActive)
			changed |= ApplyAnchor(FirstAnchor, 0, outgoing: true);

		if (LastAnchor.IsActive && Nodes.Count > 1)
			changed |= ApplyAnchor(LastAnchor, Nodes.Count - 1, outgoing: false);

		// Подпись считается заново: ApplyAnchor мог снять привязку, если цель
		// исчезла, и тогда прежнее значение уже не описывает состояние.
		_anchorsAppliedVersion = ComputeAnchorSignature();

		return changed;
	}


	/// <summary>
	/// Подтягивает один узел.
	/// outgoing = true для первого узла: линия УХОДИТ от него, значит
	/// задаётся ControlNext. Для последнего линия ПРИХОДИТ, значит ControlPrev.
	/// </summary>
	private bool ApplyAnchor(ShapeAnchor anchor, int nodeIndex, bool outgoing)
	{
		if (!ShapeAnchorGeometry.TryResolve(anchor, out var worldPoint, out var outwardWorld))
		{
			// Цель исчезла или её узлов стало меньше — связь снимается.
			anchor.Clear();
			return true;
		}

		// Цель не может быть сама кривая: замкнутая на себя связь строила бы
		// координаты из самой себя и не сходилась бы.
		if (ReferenceEquals(anchor.Target, this))
		{
			anchor.Clear();
			return true;
		}

		var node = Nodes[nodeIndex];

		var newPosition = WorldToLocal(worldPoint);
		bool changed = Distance(node.Position, newPosition) > 1e-4f;

		node.Position = newPosition;

		if (!anchor.TangentToEdge)
			return changed;

		// Направление наружу переводится в локальные координаты как ВЕКТОР:
		// разностью двух отображённых точек, иначе к нему применился бы перенос.
		var shiftedWorld = new SKPoint(
			worldPoint.X + outwardWorld.X,
			worldPoint.Y + outwardWorld.Y);

		var shiftedLocal = WorldToLocal(shiftedWorld);

		var localDir = Normalize(new SKPoint(
			shiftedLocal.X - newPosition.X,
			shiftedLocal.Y - newPosition.Y));


		if (Length(localDir) < 1e-6f)
			return changed;

		int neighbourIndex = outgoing ? nodeIndex + 1 : nodeIndex - 1;

		if (neighbourIndex < 0 || neighbourIndex >= Nodes.Count)
			return changed;

		float distance = Distance(newPosition, Nodes[neighbourIndex].Position);

		if (distance < 1e-4f)
			return changed;

		float stub = distance * MathF.Max(0.05f, AnchorStubFactor);

		var control = new SKPoint(
			newPosition.X + localDir.X * stub,
			newPosition.Y + localDir.Y * stub);

		if (outgoing)
		{
			changed |= Distance(node.ControlNext, control) > 1e-4f;
			node.ControlNext = control;
		}
		else
		{
			changed |= Distance(node.ControlPrev, control) > 1e-4f;
			node.ControlPrev = control;
		}

		// Прямой узел не рисует кривую: без смены типа выступ не появится.
		if (node.Type == CurveNodeType.Straight)
			node.Type = CurveNodeType.Sharp;

		return changed;
	}

	/// <summary>
	/// Задать привязку конца. index 0 — первый узел, иначе последний.
	/// Возвращает true, если привязка изменилась.
	/// </summary>
	public bool SetAnchor(bool first, Shape? target, ShapeAnchorKind kind, int index, Guid nodeId = default)
	{
		var anchor = first ? FirstAnchor : LastAnchor;

		if (ReferenceEquals(target, this))
			return false;

		// Сравнение идёт по идентификатору, когда он есть: номер того же
		// узла меняется после вставки или удаления соседа.
		bool same = ReferenceEquals(anchor.Target, target)
			&& anchor.Kind == kind
			&& (nodeId != Guid.Empty ? anchor.NodeId == nodeId : anchor.Index == index);

		if (same)
			return false;

		anchor.Target = target;
		anchor.Kind = target == null ? ShapeAnchorKind.None : kind;
		anchor.Index = index;
		anchor.NodeId = target == null ? Guid.Empty : nodeId;

		InvalidateAnchors();
		return true;
	}

	/// <summary>Снять привязку конца.</summary>
	public bool ClearAnchor(bool first)
	{
		var anchor = first ? FirstAnchor : LastAnchor;

		if (!anchor.IsActive)
			return false;

		anchor.Clear();
		InvalidateAnchors();
		return true;
	}

	/// <summary>
	/// Подпись входных данных привязок. Пересчёт запускается при её изменении.
	///
	/// Включает геометрию целей и собственную мировую трансформацию: положение
	/// конца зависит и от того, где стоит цель, и от того, где стоит сама кривая,
	/// потому что мировая точка переводится в локальные координаты кривой.
	/// </summary>
	private int ComputeAnchorSignature()
	{
		var hash = new HashCode();

		hash.Add(Nodes.Count);
		hash.Add(AnchorStubFactor);

		AddAnchorToSignature(ref hash, FirstAnchor);
		AddAnchorToSignature(ref hash, LastAnchor);

		var m = GetWorldTransform();

		hash.Add(m.ScaleX);
		hash.Add(m.SkewX);
		hash.Add(m.TransX);
		hash.Add(m.SkewY);
		hash.Add(m.ScaleY);
		hash.Add(m.TransY);

		// Соседние узлы задают длину выступа, поэтому их перемещение тоже
		// требует пересчёта.
		if (Nodes.Count > 1)
		{
			hash.Add(Nodes[1].Position.X);
			hash.Add(Nodes[1].Position.Y);
			hash.Add(Nodes[^2].Position.X);
			hash.Add(Nodes[^2].Position.Y);
		}

		return hash.ToHashCode();
	}

	private static void AddAnchorToSignature(ref HashCode hash, ShapeAnchor anchor)
	{
		hash.Add(anchor.Kind);
		hash.Add(anchor.Index);
		hash.Add(anchor.TangentToEdge);
		hash.Add(anchor.Enabled);

		if (anchor.Target == null)
		{
			hash.Add(0);
			return;
		}

		// Форма цели и её положение в мире: конец должен переехать и при
		// правке узлов цели, и при её перемещении вместе с группой.
		hash.Add(anchor.Target.GetGeometryHash());

		var m = anchor.Target.GetWorldTransform();

		hash.Add(m.ScaleX);
		hash.Add(m.SkewX);
		hash.Add(m.TransX);
		hash.Add(m.SkewY);
		hash.Add(m.ScaleY);
		hash.Add(m.TransY);
	}

	/// <summary>
	/// Диапазон контура, содержащего узел. Нужен перечислителю точек привязки,
	/// чтобы искать соседей внутри своего контура.
	/// </summary>
	internal (int Start, int End, bool Closed) GetContourRangeFor(int nodeIndex)
	{
		foreach (var range in GetContourRanges())
		{
			if (range.Start <= nodeIndex && nodeIndex <= range.End)
				return (range.Start, range.End, range.Closed);
		}

		return (0, Math.Max(0, Nodes.Count - 1), false);
	}

	private static float Length(SKPoint v) => MathF.Sqrt(v.X * v.X + v.Y * v.Y);

	private static SKPoint Normalize(SKPoint v)
	{
		float len = Length(v);
		return len < 1e-6f ? new SKPoint(0f, 0f) : new SKPoint(v.X / len, v.Y / len);
	}

	#endregion
}

public static class CurveAdorner
{
	public readonly record struct CurveNodeInfo(CurveShape Shape, int NodeIndex, SKPoint WorldPosition);
	public readonly record struct CurveControlPointInfo(CurveShape Shape, int NodeIndex, bool IsPrev, SKPoint WorldPosition);

	public static IReadOnlyList<CurveNodeInfo> GetNodes(Shape shape)
	{
		if (shape is not CurveShape curve || !curve.IsSelected)
			return Array.Empty<CurveNodeInfo>();

		var result = new List<CurveNodeInfo>();
		for (int i = 0; i < curve.Nodes.Count; i++)
			result.Add(new CurveNodeInfo(curve, i, curve.LocalToWorld(curve.Nodes[i].Position)));
		return result;
	}

	public static IReadOnlyList<CurveControlPointInfo> GetControlPoints(Shape shape)
	{
		if (shape is not CurveShape curve || !curve.IsSelected)
			return Array.Empty<CurveControlPointInfo>();

		var result = new List<CurveControlPointInfo>();

		int last = curve.Nodes.Count - 1;

		for (int i = 0; i < curve.Nodes.Count; i++)
		{
			var n = curve.Nodes[i];

			// Ручку, которой управляет привязка, не показываем: она
			// пересчитывается при каждой отрисовке и мышью не двигается.
			bool nextLocked = i == 0 && curve.FirstAnchor.IsActive && curve.FirstAnchor.TangentToEdge;
			bool prevLocked = i == last && curve.LastAnchor.IsActive && curve.LastAnchor.TangentToEdge;

			if (n.HasPrevControl && !prevLocked)
				result.Add(new CurveControlPointInfo(curve, i, true, curve.LocalToWorld(n.ControlPrev)));

			if (n.HasNextControl && !nextLocked)
				result.Add(new CurveControlPointInfo(curve, i, false, curve.LocalToWorld(n.ControlNext)));
		}

		return result;
	}

	public static void Draw(SKCanvas canvas, MainView view, EditorInteractionContext ctx)
	{
		if (ctx.Selection.Count != 1)
			return;

		if (ctx.Selection[0] is not CurveShape curve || !curve.IsVisible || curve.IsLocked)
			return;

		if (!view.IsCurveEditMode)
			return;

		using var nodeLinkLine = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Stroke,
			StrokeWidth = 1f,
			Color = new SKColor(90, 90, 90, 45)
		};

		using var nodeLinkLineHot = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Stroke,
			StrokeWidth = 3f,
			Color = new SKColor(50, 50, 50, 120)
		};

		using var controlLine = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Stroke,
			StrokeWidth = Settings.curve_control_line_thickness,
			Color = Settings.curve_control_line_color.ToSKColor()
		};

		using var cpFill = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Fill,
			Color = Settings.curve_cp_color.ToSKColor()
		};

		using var cpStroke = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Stroke,
			StrokeWidth = 1.5f,
			Color = Settings.curve_cp_border_color.ToSKColor()
		};

		using var nodeStroke = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Stroke,
			StrokeWidth = 1.5f
		};

		using var nodeFill = new SKPaint
		{
			IsAntialias = true,
			Style = SKPaintStyle.Fill
		};

		if (ctx.Selection[0] is ImageShape)
			DrawNodeLinks(canvas, view, ctx, curve, nodeLinkLine, nodeLinkLineHot);

		foreach (var cp in GetControlPoints(curve))
		{
			var node = curve.Nodes[cp.NodeIndex];
			var p0 = view.PageToScreen(curve.LocalToWorld(node.Position));
			var p1 = view.PageToScreen(cp.WorldPosition);

			canvas.DrawLine(p0, p1, controlLine);

			float r = Settings.curve_cp_size;
			if (ctx.HotHit.Kind == HitKind.CurveControlPoint &&
				ReferenceEquals(ctx.HotHit.Shape, curve) &&
				ctx.HotHit.CurveNodeIndex == cp.NodeIndex &&
				ctx.HotHit.CurveControlIsPrev == cp.IsPrev)
			{
				r = Settings.curve_cp_focused_size;
			}

			canvas.DrawCircle(p1, r * 0.5f, cpFill);
			canvas.DrawCircle(p1, r * 0.5f, cpStroke);
		}

		foreach (var info in GetNodes(curve))
		{
			var node = curve.Nodes[info.NodeIndex];
			var sp = view.PageToScreen(info.WorldPosition);

			float size;
			if (node.Selected)
			{
				size = Settings.curve_selected_point_size;
				nodeFill.Color = Settings.curve_selected_point_color.ToSKColor();
				nodeStroke.Color = Settings.curve_selected_point_border_color.ToSKColor();
			}
			else if (ctx.HotHit.Kind == HitKind.CurveNode &&
					 ReferenceEquals(ctx.HotHit.Shape, curve) &&
					 ctx.HotHit.CurveNodeIndex == info.NodeIndex)
			{
				size = Settings.curve_focused_point_size;
				nodeFill.Color = Settings.curve_focused_point_color.ToSKColor();
				nodeStroke.Color = Settings.curve_focused_point_border_color.ToSKColor();
			}
			else
			{
				size = Settings.curve_point_size;
				nodeFill.Color = Settings.curve_point_color.ToSKColor();
				nodeStroke.Color = Settings.curve_point_border_color.ToSKColor();
			}

			var rr = SKRect.Create(sp.X - size * 0.5f, sp.Y - size * 0.5f, size, size);
			canvas.DrawOval(rr, nodeFill);
			canvas.DrawOval(rr, nodeStroke);
		}
	}

	private static void DrawNodeLinks(
		SKCanvas canvas,
		MainView view,
		EditorInteractionContext ctx,
		CurveShape curve,
		SKPaint normalPaint,
		SKPaint hotPaint)
	{
		if (curve.Nodes.Count < 2)
			return;

		for (int i = 0; i < curve.Nodes.Count; i++)
		{
			var a = curve.Nodes[i];

			int nextIndex = i + 1;
			if (nextIndex >= curve.Nodes.Count)
			{
				if (!a.CloseAfter)
					continue;

				nextIndex = 0;
			}

			if (a.BreakAfter)
				continue;

			var b = curve.Nodes[nextIndex];

			var p0 = view.PageToScreen(curve.LocalToWorld(a.Position));
			var p1 = view.PageToScreen(curve.LocalToWorld(b.Position));

			bool isHot =
				ctx.HotHit.Kind == HitKind.CurveSegment &&
				ReferenceEquals(ctx.HotHit.Shape, curve) &&
				ctx.HotHit.CurveSegmentIndex == i;

			canvas.DrawLine(p0, p1, isHot ? hotPaint : normalPaint);
		}
	}

	public static HitResult HitTest(MainView view, Shape shape, SKPoint pagePoint)
	{
		if (shape is not CurveShape curve || !curve.IsSelected || !curve.IsVisible || curve.IsLocked)
			return HitResult.None;

		float tolNode = view.ScreenPixelsToPageUnits(Settings.curve_selected_point_size);
		float tolCp = view.ScreenPixelsToPageUnits(Settings.curve_cp_focused_size);

		for (int i = 0; i < curve.Nodes.Count; i++)
		{
			var n = curve.Nodes[i];
			var nodeWorld = curve.LocalToWorld(n.Position);
			if (Geometry.Distance(nodeWorld, pagePoint) <= tolNode)
			{
				return new HitResult
				{
					Kind = HitKind.CurveNode,
					Shape = curve,
					CurveNodeIndex = i,
					PagePoint = nodeWorld
				};
			}
		}

		for (int i = 0; i < curve.Nodes.Count; i++)
		{
			var n = curve.Nodes[i];
			if (n.HasPrevControl)
			{
				var wp = curve.LocalToWorld(n.ControlPrev);
				if (Geometry.Distance(wp, pagePoint) <= tolCp)
				{
					return new HitResult
					{
						Kind = HitKind.CurveControlPoint,
						Shape = curve,
						CurveNodeIndex = i,
						CurveControlIsPrev = true,
						PagePoint = wp
					};
				}
			}

			if (n.HasNextControl)
			{
				var wp = curve.LocalToWorld(n.ControlNext);
				if (Geometry.Distance(wp, pagePoint) <= tolCp)
				{
					return new HitResult
					{
						Kind = HitKind.CurveControlPoint,
						Shape = curve,
						CurveNodeIndex = i,
						CurveControlIsPrev = false,
						PagePoint = wp
					};
				}
			}
		}

		int seg = HitTestSegment(view, curve, pagePoint);
		if (seg >= 0)
		{
			return new HitResult
			{
				Kind = HitKind.CurveSegment,
				Shape = curve,
				CurveSegmentIndex = seg,
				PagePoint = pagePoint
			};
		}

		return HitResult.None;
	}

	/// <summary>
	/// Второй узел сегмента, начинающегося узлом segStart.
	///
	/// Для последнего узла замкнутого контура это его первый узел, а не
	/// следующий по списку: контуры лежат в одном общем списке узлов,
	/// и segStart + 1 увёл бы в соседний контур.
	/// Возвращает -1, если сегмента нет (последний узел разомкнутого контура).
	/// </summary>
	public static int GetSegmentEndNode(CurveShape curve, int segStart)
	{
		if (curve == null || segStart < 0 || segStart >= curve.Nodes.Count)
			return -1;

		foreach (var range in curve.GetContourRangesForAdorner())
		{
			if (segStart < range.Start || segStart > range.End)
				continue;

			if (segStart < range.End)
				return segStart + 1;

			return range.Closed ? range.Start : -1;
		}

		return -1;
	}

	/// <summary>
	/// Можно ли тянуть сам сегмент.
	///
	/// Нужен хотя бы один НЕ прямой узел: тянуть можно только за
	/// контрольные точки, а у прямого узла их нет. Сегмент между двумя
	/// прямыми узлами — отрезок, и «точка под курсором» для него означала
	/// бы превращение отрезка в кривую, чего пользователь не просил.
	/// </summary>
	public static bool CanDragSegment(CurveShape curve, int segStart)
	{
		int end = GetSegmentEndNode(curve, segStart);
		if (end < 0)
			return false;

		return curve.Nodes[segStart].Type != CurveNodeType.Straight
			|| curve.Nodes[end].Type != CurveNodeType.Straight;
	}

	/// <summary>Точка сегмента по параметру t. Открыта операции перетаскивания.</summary>
	public static SKPoint EvaluateSegmentAt(CurveNode a, CurveNode b, float t)
		=> EvaluateSegment(a, b, t);

	/// <summary>
	/// Параметр ближайшей к точке позиции на сегменте. Грубый перебор
	/// с тем же шагом, что и в HitTestSegment, — попадание и захват
	/// обязаны считать одно и то же место.
	/// </summary>
	public static float FindSegmentParameter(CurveShape curve, int segStart, int segEnd, SKPoint pagePoint)
	{
		var a = curve.Nodes[segStart];
		var b = curve.Nodes[segEnd];

		float best = float.MaxValue;
		float bestT = 0.5f;

		const int Steps = 64;

		for (int i = 0; i <= Steps; i++)
		{
			float t = i / (float)Steps;
			float d = Geometry.Distance(curve.LocalToWorld(EvaluateSegment(a, b, t)), pagePoint);

			if (d < best)
			{
				best = d;
				bestT = t;
			}
		}

		return bestT;
	}

	public static int HitTestSegment(MainView view, CurveShape curve, SKPoint pagePoint)
	{
		if (curve.Nodes.Count < 2)
			return -1;

		float best = float.MaxValue;
		int bestIndex = -1;

		var ranges = curve.GetContourRangesForAdorner();

		foreach (var range in ranges)
		{
			for (int i = range.Start; i <= range.End; i++)
			{
				int nextIndex;

				if (i < range.End)
				{
					nextIndex = i + 1;
				}
				else
				{
					// Замыкающий сегмент ведёт к первому узлу ЭТОГО контура.
					if (!range.Closed)
						continue;

					nextIndex = range.Start;
				}

				var a = curve.Nodes[i];
				var b = curve.Nodes[nextIndex];

				for (int s = 0; s <= 64; s++)
				{
					float t = s / 64f;
					var p = EvaluateSegment(a, b, t);
					var wp = curve.LocalToWorld(p);
					float d = Geometry.Distance(wp, pagePoint);

					if (d < best)
					{
						best = d;
						bestIndex = i;
					}
				}
			}
		}

		float tol = view.ScreenPixelsToPageUnits(10f);
		return best <= tol ? bestIndex : -1;
	}

	private static SKPoint EvaluateSegment(CurveNode a, CurveNode b, float t)
	{
		var p0 = a.Position;
		var p1 = a.HasNextControl ? a.ControlNext : a.Position;
		var p2 = b.HasPrevControl ? b.ControlPrev : b.Position;
		var p3 = b.Position;

		float mt = 1f - t;
		float mt2 = mt * mt;
		float mt3 = mt2 * mt;
		float t2 = t * t;
		float t3 = t2 * t;

		return new SKPoint(
			mt3 * p0.X + 3 * mt2 * t * p1.X + 3 * mt * t2 * p2.X + t3 * p3.X,
			mt3 * p0.Y + 3 * mt2 * t * p1.Y + 3 * mt * t2 * p2.Y + t3 * p3.Y);
	}
}

public sealed class MoveCurveNodeOperation : DragOperation
{
	private readonly CurveShape shape;
	private readonly int nodeIndex;

	public MoveCurveNodeOperation(CurveShape shape, int nodeIndex)
	{
		this.shape = shape ?? throw new ArgumentNullException(nameof(shape));
		this.nodeIndex = nodeIndex;
	}

	public override DragOperationKind Kind => DragOperationKind.MoveCurvePoint;

	/// <summary>Положение узла на начало протяжки — точка отсчёта оси.</summary>
	private SKPoint _nodeStartWorld;

	/// <summary>
	/// Отрезки кривой, снятые ДО начала протяжки.
	///
	/// Снимок, а не живая геометрия. Два отрезка упираются в перетаскиваемый
	/// узел и поворачиваются вместе с ним, а узел пытается прилипнуть
	/// к их направлению — то есть к направлению, которое сам же и задаёт.
	/// Выходит положительная обратная связь и дёрганое движение.
	/// Неподвижный снимок этот круг разрывает.
	/// </summary>
	private readonly List<SnapLineCandidate> _startLines = new();

	public override void Begin(MainView view, EditorInteractionContext ctx, SKPoint pagePoint)
	{
		ResetAxisLock();

		_nodeStartWorld = nodeIndex >= 0 && nodeIndex < shape.Nodes.Count
			? shape.LocalToWorld(shape.Nodes[nodeIndex].Position)
			: pagePoint;

		_startLines.Clear();
		_startLines.AddRange(ShapeSnapGeometry.GetSnapLines(shape));
	}

	public override void Update(MainView view, EditorInteractionContext ctx, SKPoint pagePoint)
	{
		if (nodeIndex < 0 || nodeIndex >= shape.Nodes.Count)
			return;

		SKPoint target = pagePoint;
		ctx.ActiveSnap = null;

		bool isFirst = nodeIndex == 0;
		bool isLast = nodeIndex == shape.Nodes.Count - 1;

		// Привязка ищется только для концов и только если она разрешена
		// галочкой панели. Alt временно отключает — так же, как обычное
		// прилипание при перемещении фигур.
		//
		// Прежде отключал Ctrl. Он отдан ограничению по оси — тому же,
		// что и при перемещении фигур, — а роль «временно без прилипания»
		// повсюду исполняет Alt. Держать два разных клавиша для одного
		// и того же в разных режимах значило бы заставлять пользователя
		// помнить, где он находится.
		bool anchoring =
			!ctx.AltPressed &&
			((isFirst && shape.FirstAnchor.Enabled) ||
			 (isLast && shape.LastAnchor.Enabled));

		if (anchoring)
		{
			// Settings.snap_objects здесь сознательно не проверяется: связь
			// во flowchart нужна и тогда, когда обычное прилипание выключено.
			if (ObjectSnapEngine.TryFindAnchor(
					view, shape, pagePoint, Settings.snap_dist,
					out var anchorTarget, out var kind, out int index, out var anchorPoint,
					out var nodeId))
			{
				shape.SetAnchor(isFirst, anchorTarget, kind, index, nodeId);

				// Позицию всё равно ставим: адорнеры узлов рисуются до того,
				// как BuildLocalPath вызовет ApplyAnchors, и без этого узел
				// на кадр отставал бы от курсора.
				shape.MoveNode(nodeIndex, shape.WorldToLocal(anchorPoint));

				if (view.CurrentPage != null)
					view.CurrentPage.doc.need_save = true;

				MainForm.sel.NotifyGeometryChanged();
				view.Invalidate();
				return;
			}

			// Увели конец от всех точек привязки — связь снимается.
			shape.ClearAnchor(isFirst);
		}

		if (!ctx.AltPressed && Settings.snap_objects)
		{
			// Своя же кривая участвует в прилипании: её остальные узлы
			// и продолжения её отрезков — самые нужные ориентиры, когда
			// строят фигуру по себе самой. Исключается только сам
			// перетаскиваемый узел: он движется вместе с курсором
			// и был бы ближайшей целью всегда.
			var ignore = new[] { shape.LocalToWorld(shape.Nodes[nodeIndex].Position) };

			var snap = ObjectSnapEngine.SnapMovingPoint(
				view,
				new[] { shape },
				pagePoint,
				Settings.snap_dist,
				shape,
				ignore,
				_startLines);

			ctx.ActiveSnap = snap;

			if (snap != null && snap.IsValid)
				target = new SKPoint(pagePoint.X + snap.Correction.X, pagePoint.Y + snap.Correction.Y);
		}

		// Ограничение по оси: при зажатом Ctrl движение идёт только
		// в ту сторону, в которую пользователь повёл первой.
		target = ApplyAxisLock(view, ctx, _nodeStartWorld, target);

		shape.MoveNode(nodeIndex, shape.WorldToLocal(target));

		if (view.CurrentPage != null)
			view.CurrentPage.doc.need_save = true;

		MainForm.sel.NotifyGeometryChanged();
		view.Invalidate();
	}


	public override void End(MainView view, EditorInteractionContext ctx, SKPoint pagePoint)
	{
		//Update(view, ctx, pagePoint);
		ctx.ActiveSnap = null;
	}

	public override void Cancel(MainView view, EditorInteractionContext ctx)
	{
		ctx.ActiveSnap = null;
		view.Invalidate();
	}
}

public sealed class MoveSelectedCurveNodesOperation : DragOperation
{
	private readonly CurveShape shape;
	private readonly int anchorNodeIndex;
	private readonly Dictionary<int, SKPoint> startPositions = new();
	private SKPoint startPagePoint;

	public MoveSelectedCurveNodesOperation(CurveShape shape, int anchorNodeIndex)
	{
		this.shape = shape ?? throw new ArgumentNullException(nameof(shape));
		this.anchorNodeIndex = anchorNodeIndex;
	}

	public override DragOperationKind Kind => DragOperationKind.MoveCurvePoint;

	public override void Begin(MainView view, EditorInteractionContext ctx, SKPoint pagePoint)
	{
		ResetAxisLock();

		startPagePoint = pagePoint;
		startPositions.Clear();

		for (int i = 0; i < shape.Nodes.Count; i++)
		{
			if (shape.Nodes[i].Selected)
				startPositions[i] = shape.Nodes[i].Position;
		}

		if (startPositions.Count == 0 && anchorNodeIndex >= 0 && anchorNodeIndex < shape.Nodes.Count)
			startPositions[anchorNodeIndex] = shape.Nodes[anchorNodeIndex].Position;
	}

	public override void Update(MainView view, EditorInteractionContext ctx, SKPoint pagePoint)
	{
		// Ось считается в мировых координатах, а смещение узлов —
		// в локальных: у повёрнутой кривой «по горизонтали» означает
		// горизонталь листа, а не её собственной системы координат.
		var constrained = ApplyAxisLock(view, ctx, startPagePoint, pagePoint);

		var startLocal = shape.WorldToLocal(startPagePoint);
		var currentLocal = shape.WorldToLocal(constrained);
		var delta = new SKPoint(currentLocal.X - startLocal.X, currentLocal.Y - startLocal.Y);

		foreach (var kv in startPositions)
		{
			int index = kv.Key;
			var startLocalPos = kv.Value;
			shape.MoveNode(index, new SKPoint(startLocalPos.X + delta.X, startLocalPos.Y + delta.Y));
		}

		if (view.CurrentPage != null)
			view.CurrentPage.doc.need_save = true;

		MainForm.sel.NotifyGeometryChanged();
		view.Invalidate();
	}

	public override void End(MainView view, EditorInteractionContext ctx, SKPoint pagePoint)
	{
		ctx.ActiveSnap = null;
	}

	public override void Cancel(MainView view, EditorInteractionContext ctx)
	{
		foreach (var kv in startPositions)
		{
			int index = kv.Key;
			shape.MoveNode(index, kv.Value);
		}

		ctx.ActiveSnap = null;
		view.Invalidate();
	}
}

/// <summary>
/// Перетаскивание самой линии сегмента: точка, за которую взялись,
/// следует за курсором.
///
/// Узлы при этом не двигаются — меняются только контрольные точки
/// соседних узлов. Кубическая кривая в точке t равна
///     B(t) = mt3*P0 + 3*mt2*t*P1 + 3*mt*t2*P2 + t3*P3,
/// где P0 и P3 — узлы, а P1 и P2 — их рычаги. Узлы закреплены, поэтому
/// нужное смещение D точки B(t) надо разложить по P1 и P2 с весами
///     b1 = 3*mt2*t,   b2 = 3*mt*t2.
/// Из всех решений b1*dP1 + b2*dP2 = D выбирается наименьшее по норме:
///     dP1 = D*b1/(b1^2+b2^2),  dP2 = D*b2/(b1^2+b2^2).
/// Так форма меняется минимально необходимым образом, а не «уводится»
/// в сторону ближнего конца.
///
/// Если у одного из узлов рычага нет (он прямой), вся поправка уходит
/// во второй: dP = D/b. Когда прямые оба, операция вообще не начинается —
/// см. CurveAdorner.CanDragSegment.
/// </summary>
public sealed class MoveCurveSegmentOperation : DragOperation
{
	/// <summary>
	/// Насколько параметр захвата отжимается от концов сегмента.
	///
	/// У самых концов веса b1 и b2 обращаются в ноль, и деление на них
	/// швыряло бы рычаг на бесконечность от крошечного движения мыши.
	/// Вблизи узла тянуть надо сам узел, а не линию.
	/// </summary>
	private const float MinParameter = 0.08f;

	private readonly CurveShape _shape;
	private readonly int _startNode;
	private readonly int _endNode;

	private float _t;

	// Исходные рычаги в локальных координатах. Смещение считается ОТ них,
	// а не от текущих: накапливай мы поправку на поправку, точка уползала
	// бы от курсора за несколько кадров перетаскивания.
	private SKPoint _startControlNext;
	private SKPoint _startControlPrev;
	private SKPoint _startPositionA;
	private SKPoint _startPositionB;

	private bool _movesNext;
	private bool _movesPrev;
	private bool _begun;

	public override DragOperationKind Kind => DragOperationKind.MoveCurveSegment;

	public MoveCurveSegmentOperation(CurveShape shape, int startNode, int endNode)
	{
		_shape = shape ?? throw new ArgumentNullException(nameof(shape));
		_startNode = startNode;
		_endNode = endNode;
	}

	public override void Begin(MainView view, EditorInteractionContext ctx, SKPoint pagePoint)
	{
		if (!IsValid())
			return;

		var a = _shape.Nodes[_startNode];
		var b = _shape.Nodes[_endNode];

		_t = Math.Clamp(
			CurveAdorner.FindSegmentParameter(_shape, _startNode, _endNode, pagePoint),
			MinParameter,
			1f - MinParameter);

		// Рычаг существует только у непрямого узла. У прямого его нет,
		// и создавать его на ходу нельзя: пользователь просил тянуть
		// сегменты, а не превращать прямые узлы в гладкие.
		_movesNext = a.Type != CurveNodeType.Straight;
		_movesPrev = b.Type != CurveNodeType.Straight;

		_startPositionA = a.Position;
		_startPositionB = b.Position;
		_startControlNext = a.HasNextControl ? a.ControlNext : a.Position;
		_startControlPrev = b.HasPrevControl ? b.ControlPrev : b.Position;

		_begun = _movesNext || _movesPrev;
	}

	public override void Update(MainView view, EditorInteractionContext ctx, SKPoint pagePoint)
	{
		if (!_begun || !IsValid())
			return;

		var target = _shape.WorldToLocal(pagePoint);

		float mt = 1f - _t;
		float b1 = 3f * mt * mt * _t;
		float b2 = 3f * mt * _t * _t;

		// Где точка была бы сейчас при ИСХОДНЫХ рычагах.
		var basePoint = Bezier(_startPositionA, _startControlNext, _startControlPrev, _startPositionB, _t);

		float dx = target.X - basePoint.X;
		float dy = target.Y - basePoint.Y;

		float wNext = 0f;
		float wPrev = 0f;

		if (_movesNext && _movesPrev)
		{
			float denom = b1 * b1 + b2 * b2;
			if (denom < 1e-6f)
				return;

			wNext = b1 / denom;
			wPrev = b2 / denom;
		}
		else if (_movesNext)
		{
			if (b1 < 1e-6f)
				return;

			wNext = 1f / b1;
		}
		else
		{
			if (b2 < 1e-6f)
				return;

			wPrev = 1f / b2;
		}

		// MoveControlPoint, а не присваивание полю: он же держит зеркало
		// у гладкого узла, и без него перетаскивание сегмента ломало бы
		// плавность стыка с соседним сегментом.
		if (_movesNext)
		{
			_shape.MoveControlPoint(_startNode, false, new SKPoint(
				_startControlNext.X + dx * wNext,
				_startControlNext.Y + dy * wNext));
		}

		if (_movesPrev)
		{
			_shape.MoveControlPoint(_endNode, true, new SKPoint(
				_startControlPrev.X + dx * wPrev,
				_startControlPrev.Y + dy * wPrev));
		}

		if (view.CurrentPage != null)
			view.CurrentPage.doc.need_save = true;

		MainForm.sel.NotifyGeometryChanged();
		view.Invalidate();
	}

	public override void End(MainView view, EditorInteractionContext ctx, SKPoint pagePoint)
	{
		Update(view, ctx, pagePoint);
	}

	public override void Cancel(MainView view, EditorInteractionContext ctx)
	{
		if (!_begun || !IsValid())
			return;

		if (_movesNext)
			_shape.MoveControlPoint(_startNode, false, _startControlNext);

		if (_movesPrev)
			_shape.MoveControlPoint(_endNode, true, _startControlPrev);

		MainForm.sel.NotifyGeometryChanged();
		view.Invalidate();
	}

	private bool IsValid()
	{
		return _startNode >= 0 && _startNode < _shape.Nodes.Count
			&& _endNode >= 0 && _endNode < _shape.Nodes.Count
			&& _startNode != _endNode;
	}

	private static SKPoint Bezier(SKPoint p0, SKPoint p1, SKPoint p2, SKPoint p3, float t)
	{
		float mt = 1f - t;
		float mt2 = mt * mt;
		float mt3 = mt2 * mt;
		float t2 = t * t;
		float t3 = t2 * t;

		return new SKPoint(
			mt3 * p0.X + 3f * mt2 * t * p1.X + 3f * mt * t2 * p2.X + t3 * p3.X,
			mt3 * p0.Y + 3f * mt2 * t * p1.Y + 3f * mt * t2 * p2.Y + t3 * p3.Y);
	}
}

public sealed class MoveCurveControlPointOperation : DragOperation
{
	private readonly CurveShape shape;
	private readonly int nodeIndex;
	private readonly bool isPrev;

	public MoveCurveControlPointOperation(CurveShape shape, int nodeIndex, bool isPrev)
	{
		this.shape = shape ?? throw new ArgumentNullException(nameof(shape));
		this.nodeIndex = nodeIndex;
		this.isPrev = isPrev;
	}

	public override DragOperationKind Kind => DragOperationKind.MoveCurveControlPoint;

	public override void Update(MainView view, EditorInteractionContext ctx, SKPoint pagePoint)
	{
		if (nodeIndex < 0 || nodeIndex >= shape.Nodes.Count)
			return;

		// Ручку выхода задаёт привязка, а не мышь: ApplyAnchors пересчитает
		// её при следующем построении пути, и перетаскивание всё равно
		// не удержалось бы.
		//
		// isPrev = false — это ControlNext, ручка ВЫХОДА из первого узла.
		// isPrev = true — ControlPrev, ручка ВХОДА в последний узел.
		bool locked =
			(!isPrev && nodeIndex == 0 &&
			 shape.FirstAnchor.IsActive && shape.FirstAnchor.TangentToEdge) ||
			(isPrev && nodeIndex == shape.Nodes.Count - 1 &&
			 shape.LastAnchor.IsActive && shape.LastAnchor.TangentToEdge);

		if (locked)
			return;

		SKPoint target = pagePoint;
		ctx.ActiveSnap = null;


		if (!ctx.CtrlPressed && Settings.snap_objects)
		{
			var snap = ObjectSnapEngine.SnapMovingPoint(view, new[] { shape }, pagePoint, Settings.snap_dist);
			ctx.ActiveSnap = snap;
			if (snap != null && snap.IsValid)
				target = new SKPoint(pagePoint.X + snap.Correction.X, pagePoint.Y + snap.Correction.Y);
		}

		shape.MoveControlPoint(nodeIndex, isPrev, shape.WorldToLocal(target));

		if (view.CurrentPage != null)
			view.CurrentPage.doc.need_save = true;

		MainForm.sel.NotifyGeometryChanged();
		view.Invalidate();
	}

	public override void End(MainView view, EditorInteractionContext ctx, SKPoint pagePoint)
	{
		ctx.ActiveSnap = null;
	}

	public override void Cancel(MainView view, EditorInteractionContext ctx)
	{
		ctx.ActiveSnap = null;
		view.Invalidate();
	}
}

public sealed class InsertCurvePointOperation : DragOperation
{
	private readonly CurveShape shape;
	private readonly int segmentIndex;

	public InsertCurvePointOperation(CurveShape shape, int segmentIndex)
	{
		this.shape = shape ?? throw new ArgumentNullException(nameof(shape));
		this.segmentIndex = segmentIndex;
	}

	public override DragOperationKind Kind => DragOperationKind.InsertCurvePoint;

	public override void Begin(MainView view, EditorInteractionContext ctx, SKPoint pagePoint)
	{
		shape.InsertNodeOnSegment(segmentIndex, shape.WorldToLocal(pagePoint));

		if (view.CurrentPage != null)
			view.CurrentPage.doc.need_save = true;

		MainForm.sel.NotifyGeometryChanged();
		view.Invalidate();
	}
}