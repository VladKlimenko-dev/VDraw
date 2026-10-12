using SkiaSharp;
using System.Windows.Forms;

using VDraw.Animation;
using VDraw.Documents;
using VDraw.Editing.Images;
using VDraw.History;
using VDraw.Shapes;
using VDraw.Ui;

namespace VDraw.Editing;

/// <summary>
/// Кисть формата: применяет обводку и заливку из буфера к фигуре под курсором.
///
/// Инструмент, а не разовая команда, потому что источник и приёмник
/// выбираются в разное время и разными способами: формат берётся у выделенной
/// фигуры кнопкой на панели, а применяется щелчком по холсту. Пока кисть
/// включена, можно покрасить сколько угодно фигур подряд — это и есть смысл
/// такого инструмента, иначе он ничем не отличался бы от обычной вставки.
///
/// Выход: Esc, правая кнопка или повторный щелчок по кнопке формата.
/// </summary>
public sealed class FormatPainterTool : IEditorTool
{
	#region Свойства

	public ShapeToolKind Kind => ShapeToolKind.FormatPainter;

	#endregion

	#region Обработка ввода

	public void OnMouseDown(MainView view, EditorInteractionContext ctx, SKPoint pagePoint, MouseButtons button)
	{
		if (view == null || view.CurrentPage == null)
			return;

		// Правая кнопка — выход без изменений. Так же ведут себя остальные
		// инструменты выбора цели.
		if (button != MouseButtons.Left)
		{
			Finish(view);
			return;
		}

		if (!ShapeFormatClipboard.HasFormat)
		{
			Finish(view);
			return;
		}

		var picked = FindShapeAt(view, ctx, pagePoint);

		if (picked == null)
			return;

		// Отдельный шаг истории на каждую покрашенную фигуру: пользователь
		// сделал столько щелчков, сколько фигур покрасил, и отменять ожидает
		// по одному.
		if (ShapeFormatClipboard.Apply(new[] { picked }, view.Undo) == 0)
			return;

		view.CurrentPage.doc.need_save = true;

		view.mainForm?.RebuildPropertyPanel();
		view.Invalidate();
	}

	public void OnMouseMove(MainView view, EditorInteractionContext ctx, SKPoint pagePoint)
	{
		// Курсор-крест: показывает, что щелчок сейчас не выделит фигуру,
		// а изменит её.
		view.Cursor = Cursors.Cross;
	}

	public void OnMouseUp(MainView view, EditorInteractionContext ctx, SKPoint pagePoint, MouseButtons button)
	{
	}

	public void OnKeyDown(MainView view, EditorInteractionContext ctx, Keys keyData)
	{
		if (keyData == Keys.Escape)
			Finish(view);
	}

	public void DrawOverlay(SKCanvas canvas, MainView view, EditorInteractionContext ctx)
	{
	}

	#endregion

	#region Вспомогательные методы

	/// <summary>
	/// Возвращает в режим выбора. Буфер формата при этом НЕ очищается:
	/// пользователь мог выйти случайно, и терять взятый образец из-за этого
	/// было бы обидно.
	/// </summary>
	private static void Finish(MainView view)
	{
		view.SetTool(ShapeToolKind.Select);
		view.mainForm?.UpdateFormatButton();
		view.Invalidate();
	}

	/// <summary>
	/// Фигура под курсором.
	///
	/// Сначала берётся результат общего hit-теста, уже посчитанный MainView
	/// перед вызовом инструмента. Он учитывает порядок перекрытия и
	/// попадание по обводке, а не только по габаритам.
	/// </summary>
	private static Shape? FindShapeAt(MainView view, EditorInteractionContext ctx, SKPoint pagePoint)
	{
		var hit = ctx.MouseDownHit;

		if (hit.Shape != null && hit.Shape is not GuideLine)
			return hit.Shape;

		// Запасной путь: hit-тест мог вернуть ручку выделения или пустоту,
		// хотя фигура под курсором есть.
		float tolerance = view.HitToleranceUom();

		foreach (var shape in view.GetShapesFrontToBack())
		{
			if (shape is GuideLine || !shape.IsVisible || shape.IsLocked)
				continue;

			if (shape.HitTest(pagePoint, tolerance, view.MmPerPageUnit()))
				return shape;
		}

		return null;
	}

	#endregion
}