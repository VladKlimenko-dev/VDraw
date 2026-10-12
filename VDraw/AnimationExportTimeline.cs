using System.ComponentModel;

namespace VDraw.Editing;

/// <summary>
/// Линейка кадров анимации.
///
/// Показывает всю длину анимации, отмечает кадры с ключами, выделяет
/// экспортируемый диапазон и позволяет перетаскивать текущий кадр.
/// Отдельный элемент, а не TrackBar: стандартный ползунок не умеет ни
/// подсвечивать диапазон, ни отмечать ключи, а без этого линейка не
/// отвечает на главный вопрос — что именно попадёт в файл.
/// </summary>
[System.ComponentModel.ToolboxItem(false)]
public sealed class AnimationExportTimeline : Control
{
	private int _frameCount = 1;
	private int _currentFrame = 1;
	private int _firstFrame = 1;
	private int _lastFrame = 1;

	private bool _dragging;

	/// <summary>Кадры, на которых есть ключи. Отмечаются рисками.</summary>
	private HashSet<int> _keyFrames = new();

	public event Action<int>? FrameChanged;

	public AnimationExportTimeline()
	{
		DoubleBuffered = true;
		Height = 44;
	}

	public void SetAnimation(int frameCount, HashSet<int> keyFrames)
	{
		_frameCount = Math.Max(1, frameCount);
		_keyFrames = keyFrames;

		Invalidate();
	}

	public void SetRange(int first, int last)
	{
		_firstFrame = Math.Clamp(first, 1, _frameCount);
		_lastFrame = Math.Clamp(last, 1, _frameCount);

		Invalidate();
	}

	[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
	public int CurrentFrame
	{
		get => _currentFrame;
		set
		{
			int clamped = Math.Clamp(value, 1, _frameCount);

			if (clamped == _currentFrame)
				return;

			_currentFrame = clamped;
			Invalidate();
		}
	}

	#region Ввод

	protected override void OnMouseDown(MouseEventArgs e)
	{
		base.OnMouseDown(e);

		if (e.Button != MouseButtons.Left)
			return;

		_dragging = true;
		SetFrameFromX(e.X);
	}

	protected override void OnMouseMove(MouseEventArgs e)
	{
		base.OnMouseMove(e);

		if (_dragging)
			SetFrameFromX(e.X);
	}

	protected override void OnMouseUp(MouseEventArgs e)
	{
		base.OnMouseUp(e);

		_dragging = false;
	}

	private void SetFrameFromX(int x)
	{
		var track = GetTrackRect();

		if (track.Width <= 0)
			return;

		float t = Math.Clamp((x - track.X) / (float)track.Width, 0f, 1f);

		int frame = 1 + (int)MathF.Round(t * (_frameCount - 1));

		if (frame == _currentFrame)
			return;

		_currentFrame = frame;

		Invalidate();
		FrameChanged?.Invoke(frame);
	}

	#endregion

	#region Отрисовка

	private Rectangle GetTrackRect() =>
		new(8, 10, Math.Max(1, ClientSize.Width - 16), 18);

	private int FrameToX(int frame)
	{
		var track = GetTrackRect();

		if (_frameCount <= 1)
			return track.X;

		return track.X + (int)MathF.Round(track.Width * (frame - 1) / (float)(_frameCount - 1));
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		base.OnPaint(e);

		var g = e.Graphics;
		var track = GetTrackRect();

		using (var back = new SolidBrush(Color.FromArgb(238, 238, 238)))
			g.FillRectangle(back, track);

		// Экспортируемый диапазон — единственное, ради чего эта линейка
		// отличается от обычного ползунка: видно, какая часть анимации
		// попадёт в файл.
		int x1 = FrameToX(_firstFrame);
		int x2 = FrameToX(_lastFrame);

		using (var range = new SolidBrush(Color.FromArgb(70, 160, 255)))
			g.FillRectangle(range, x1, track.Y, Math.Max(2, x2 - x1), track.Height);

		using (var border = new Pen(Color.FromArgb(160, 160, 160)))
			g.DrawRectangle(border, track);

		using (var keyPen = new Pen(Color.FromArgb(60, 60, 60)))
		{
			foreach (int frame in _keyFrames)
			{
				if (frame < 1 || frame > _frameCount)
					continue;

				int x = FrameToX(frame);
				g.DrawLine(keyPen, x, track.Bottom + 2, x, track.Bottom + 10);
			}
		}

		int cursor = FrameToX(_currentFrame);

		using (var pen = new Pen(Color.FromArgb(200, 40, 40), 2f))
			g.DrawLine(pen, cursor, track.Y - 4, cursor, track.Bottom + 4);
	}

	#endregion
}

