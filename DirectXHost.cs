﻿using System;
using System.Windows.Forms;
using SharpDX.Windows;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Security.Permissions;
using System.Threading.Tasks;

namespace DirectXHost
{
	public class DirectXHost : IDisposable
	{
		[DllImport("user32")]
		private static extern int PrintWindow(IntPtr hwnd, IntPtr hdcBlt, UInt32 nFlags);

		private Form _overlayForm;
		private PictureBox _overlayTarget;
		private RenderForm _dxForm;
		private GraphicsD3D11 _graphics;
		private bool UserResized { get; set; }
		private Size ClientSize { get; set; }

		public bool IsDisposed { get; private set; }

		#region Events
		public event EventHandler HostWindowClosed;
		public event EventHandler HostWindowCreated;
		protected void OnHostWindowCreated()
		{
			if (HostWindowCreated != null)
			{
				HostWindowCreated(this._dxForm, new EventArgs());
			}
		}

		protected void OnHostWindowClosed()
		{
			if (HostWindowClosed != null)
			{
				HostWindowClosed(this._dxForm, new EventArgs());
			}
		}
		#endregion


		public void Exit()
		{
			throw new NotImplementedException();
		}

		public async Task Run()
		{
			try
			{
				await Settings.Load();
				InitializeRenderForm();

				_graphics = new GraphicsD3D11();
				_graphics.Initialise(_dxForm, true);
				_dxForm.UserResized += (sender, args) =>
				{
					var renderForm = sender as RenderForm;
					ClientSize = new Size(renderForm.ClientSize.Width, renderForm.ClientSize.Height);
					UserResized = true;
				};
				_overlayForm.Show();
				RenderLoop.Run(_dxForm, RenderCallback);
			}
			catch (ArgumentException ex)
			{
				MessageBox.Show(string.Format("Exception running DirectX host window.\r\n{0}", ex.Message), "DirectX Host", MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
			catch (Exception ex)
			{
				MessageBox.Show(string.Format("Exception running DirectX host window.\r\n{0}", ex.Message), "DirectX Host", MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
		}

		// Set the bitmap object to the size of the screen
		private Bitmap _bmpScreenshot;
		Bitmap bmpScreenshot
		{
			get => _bmpScreenshot;
			set
			{
				_bmpScreenshot = value;
				_overlayTarget.Image = value;
				updateImageBounds();
			}
		}

		Graphics gfxScreenshot;
		private void RenderCallback()
		{
			Update();
			Draw();
			gfxScreenshot = Graphics.FromImage(bmpScreenshot);
			IntPtr dc = gfxScreenshot.GetHdc();
			PrintWindow(_dxForm.Handle, dc, 0);
			gfxScreenshot.ReleaseHdc();
			gfxScreenshot.Dispose();
			_overlayTarget.Invalidate();
		}

		class OverlayForm : Form
		{
			public OverlayForm() : base()
			{
				DoubleBuffered = true;
			}
		}

		bool _drag = false;
		Point _wstart, _mstart;

		private void updateImageBounds()
		{
			_overlayTarget.Size = _dxForm.Size;
			int BorderWidth = (_dxForm.Width - _dxForm.ClientSize.Width) / 2;
			int TitlebarHeight = (_dxForm.Height - _dxForm.ClientSize.Height) - BorderWidth;
			_overlayTarget.Location = new Point(-BorderWidth, -TitlebarHeight);
			_overlayTarget.Width = _dxForm.Width - BorderWidth;
			_overlayTarget.Height = _dxForm.Height - BorderWidth;
			_overlayTarget.Invalidate();
		}

		private void InitializeRenderForm()
		{
			_dxForm = new RenderForm(Constants.WindowTitle);
			_dxForm.HandleCreated += WindowHandleCreated;
			_dxForm.HandleDestroyed += WindowHandleDestroyed;
			_dxForm.MinimumSize = new Size(Constants.StartWidth, Constants.StartHeight);
			_dxForm.ClientSize = Settings.savePositions ? Settings.containerRect.Size : _dxForm.MinimumSize;
			_dxForm.StartPosition = Settings.savePositions ? FormStartPosition.Manual : FormStartPosition.CenterScreen;
			if (Settings.savePositions) _dxForm.Location = Settings.containerRect.Point;
			_dxForm.FormBorderStyle = FormBorderStyle.SizableToolWindow;
			_dxForm.GotFocus += (s, e) => ShouldShowOverlayFrame(true);
			_dxForm.LostFocus += (s, e) => ShouldShowOverlayFrame(false);
			_dxForm.TopMost = false;
			_dxForm.HelpRequested += _dxForm_HelpRequested;
			_dxForm.Menu = GetMenu();
			_dxForm.UserResized += (sender, args) => { Settings.containerRect.Size = _dxForm.ClientSize; Settings.Save(); };
			_dxForm.LocationChanged += (sender, args) => { Settings.containerRect.Point = _dxForm.Location; Settings.Save(); };

			_overlayForm = new OverlayForm();
			_overlayForm.Text = _overlayForm.Name = "Discord Overlay";
			_overlayForm.MinimumSize = new Size(100, 50);
			_overlayForm.ClientSize = Settings.savePositions ? Settings.overlayRect.Size : new Size(Constants.OverlayStartWidth, Constants.OverlayStartHeight);
			_overlayForm.StartPosition = Settings.savePositions ? FormStartPosition.Manual : FormStartPosition.CenterScreen;
			if (Settings.savePositions) _overlayForm.Location = Settings.overlayRect.Point;
			_overlayForm.BackColor = Settings.transparencyKey;
			_overlayForm.TransparencyKey = Settings.transparencyKey;
			_overlayForm.TopMost = Settings.topMost;
			_overlayForm.ShowIcon = false;
			_overlayForm.MinimizeBox = true;
			_overlayForm.MaximizeBox = true;
			_overlayForm.BackgroundImageLayout = ImageLayout.None;
			_overlayForm.FormBorderStyle = FormBorderStyle.Sizable;
			_overlayForm.FormClosing += (s, e) => _dxForm.Close();
			_overlayForm.GotFocus += (s, e) => ShouldShowOverlayFrame(true);
			_overlayForm.LostFocus += (s, e) => ShouldShowOverlayFrame(false);
			_overlayTarget = new PictureBox();
			_overlayTarget.SizeMode = PictureBoxSizeMode.Normal;
			_overlayTarget.BackColor = Color.Red;
			_overlayTarget.Anchor = AnchorStyles.Top | AnchorStyles.Left;
			_overlayForm.Controls.Add(_overlayTarget);
			_overlayForm.ResizeEnd += (sender, args) => { Settings.overlayRect.Size = _overlayForm.ClientSize; Settings.Save(); };
			_overlayForm.LocationChanged += (sender, args) => { Settings.overlayRect.Point = _overlayForm.Location; Settings.Save(); };

			// Set the bitmap object to the size of the screen
			bmpScreenshot = new Bitmap(_dxForm.Width, _dxForm.Height, PixelFormat.Format32bppArgb);

			_dxForm.MouseDown += (s, e) =>
			{
				_drag = e.Button == MouseButtons.Left;
				_mstart = _dxForm.PointToScreen(e.Location);
				_wstart = _dxForm.Location;
			};
			_dxForm.MouseUp += (s, e) => _drag = e.Button == MouseButtons.Left ? false : _drag;
			_dxForm.MouseMove += (s, e) =>
			{
				if (!_drag) return;
				var newPoint = e.Location;
				var delta = new Point(newPoint.X - _mstart.X, newPoint.Y - _mstart.Y);
				_dxForm.Location = _dxForm.PointToScreen(new Point(_wstart.X + delta.X, _wstart.Y + delta.Y));
			};

			_overlayForm.MouseDown += (s, e) =>
			{
				_drag = e.Button == MouseButtons.Left;
				_mstart = _overlayForm.PointToScreen(e.Location);
				_wstart = _overlayForm.Location;
			};
			_overlayForm.MouseUp += (s, e) => _drag = e.Button == MouseButtons.Left ? false : _drag;
			_overlayForm.MouseMove += (s, e) =>
			{
				if (!_drag) return;
				var newPoint = e.Location;
				var delta = new Point(newPoint.X - _mstart.X, newPoint.Y - _mstart.Y);
				_overlayForm.Location = _overlayForm.PointToScreen(new Point(_wstart.X + delta.X, _wstart.Y + delta.Y));
			};
		}

		private void _dxForm_HelpRequested(object sender, EventArgs eventArgs)
		{
			MessageBox.Show(_dxForm, @"Overlay Clickable: Enables/Disables ability to interact with the Overlay Window.

Save Window Positions: Saves the Overlay and Container sizes and screen positions.

Transparency Colour: The colour used as a transparency key for hiding the background. Windows does not support an Alpha channel, so it has to use a defined colour to control clipping.

If you have issues with the window positions/sizes, delete the 'props.bin' file that is generated in the program's folder. This will reset the program settings.
", "Help", MessageBoxButtons.OK, MessageBoxIcon.Question);
		}
		private int ColorToBgr(Color color) => (color.B << 16) | (color.G << 8) | color.R;
		private MainMenu GetMenu() => new MainMenu(new MenuItem[]
			{
				new MenuItem("?", _dxForm_HelpRequested),
				new MenuItem($"{(Settings.overlayClickable ? '☑' : '☐')} Overlay Clickable", (s,e) => {
					Settings.overlayClickable = !Settings.overlayClickable;
					_dxForm.Menu = GetMenu();
					ShouldShowOverlayFrame(Settings.overlayClickable);
					Settings.Save();
				}),
				new MenuItem($"{(Settings.savePositions ? '☑' : '☐')} Save Window Positions", (s,e) => {
					Settings.savePositions = !Settings.savePositions;
					_dxForm.Menu = GetMenu();
					Settings.Save();
				}),
				new MenuItem($"{(Settings.topMost ? '☑' : '☐')} Always On Top", (s,e) => {
					Settings.topMost = !Settings.topMost;
					_dxForm.Menu = GetMenu();
					Settings.Save();
				}),
				new MenuItem("Transparency Colour", (s,e) =>
				{
					var dialog = new ColorDialog();
					dialog.Color = Settings.transparencyKey;
					dialog.SolidColorOnly = true;
					dialog.AnyColor = true;
					dialog.FullOpen = true;
					dialog.CustomColors = new int[] { ColorToBgr(Constants.DefaultTransparencyKey), ColorToBgr(Settings.transparencyKey) };
					if(dialog.ShowDialog(_dxForm) == DialogResult.OK)
					{
						Settings.transparencyKey = dialog.Color;
						Settings.Save();
						ShouldShowOverlayFrame(true);
					}
				}),
				new MenuItem("            Version 2.0")
			});
		private void ShouldShowOverlayFrame(bool show)
		{
			if (show && Settings.overlayClickable)
			{
				_overlayForm.AllowTransparency = false;
				_overlayForm.FormBorderStyle = FormBorderStyle.Sizable;
			}
			else
			{
				try
				{
					_overlayForm.AllowTransparency = true;
					_overlayForm.FormBorderStyle = FormBorderStyle.None;
					_overlayForm.BackColor = Settings.transparencyKey;
					_overlayForm.TransparencyKey = Settings.transparencyKey;
				}
				catch (Exception) { }
			}
		}

		private void WindowHandleDestroyed(object sender, EventArgs e)
		{
			Dispose();
			IsDisposed = true;
			OnHostWindowClosed();
		}

		private void WindowHandleCreated(object sender, EventArgs e)
		{
			OnHostWindowCreated();
		}

		public void Update()
		{
			if (!UserResized) return;
			_graphics.ResizeGraphics(ClientSize.Width, ClientSize.Height);
			UserResized = false;
			bmpScreenshot?.Dispose();
			if (ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
			//// Set the bitmap object to the size of the screen
			bmpScreenshot = new Bitmap(_dxForm.Width, _dxForm.Height, PixelFormat.Format32bppArgb);
		}

		public void Draw()
		{
			_graphics.ClearRenderTargetView();
			_graphics.PresentSwapChain();
		}

		#region IDisposable Support

		protected virtual void Dispose(bool disposing)
		{
			if (!IsDisposed)
			{
				if (disposing)
				{
					// TODO: dispose managed state (managed objects).
					_dxForm.Dispose();
				}

				_graphics.Dispose();
				IsDisposed = true;
			}
		}

		// TODO: override a finalizer only if Dispose(bool disposing) above has code to free unmanaged resources.
		~DirectXHost()
		{
			// Do not change this code. Put cleanup code in Dispose(bool disposing) above.
			Dispose(false);
		}

		// This code added to correctly implement the disposable pattern.
		public void Dispose()
		{
			// Do not change this code. Put cleanup code in Dispose(bool disposing) above.
			Dispose(true);
			// TODO: uncomment the following line if the finalizer is overridden above.
			GC.SuppressFinalize(this);
		}
		#endregion

	}
}
