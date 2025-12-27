using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Drawing;
using System.IO;
using OnTopReplica.WindowSeekers;

namespace OnTopReplica.StartupOptions {

    /// <summary>
    /// Represents startup options that can be set via CLI scripting (or other stuff).
    /// </summary>
    class Options {

        public Options() {
            Status = CliStatus.Ok;
            Opacity = 255;
            DisableChrome = false;
            MustBeVisible = false;
            Fullscreen = false;
        }

        #region Position and size

        public Point? StartLocation { get; set; }

        public ScreenPosition? StartPositionLock { get; set; }

        public Size? StartSize { get; set; }

        public int? StartWidth { get; set; }

        public int? StartHeight { get; set; }

        #endregion

        #region Window cloning

        public IntPtr? WindowId { get; set; }

        public string WindowTitle { get; set; }

        public string WindowClass { get; set; }

        public ThumbnailRegion Region { get; set; }

        public bool MustBeVisible { get; set; }

        #endregion

        #region Options

        public bool EnableClickForwarding { get; set; }

        public bool EnableClickThrough { get; set; }

        public byte Opacity { get; set; }

        public bool DisableChrome { get; set; }

        public bool Fullscreen { get; set; }

        #endregion

        #region Debug info

        StringBuilder _sb = new StringBuilder();
        TextWriter _sbWriter;

        public CliStatus Status { get; set; }

        /// <summary>
        /// Gets a debug message writer.
        /// </summary>
        public TextWriter DebugMessageWriter {
            get {
                if (_sbWriter == null) {
                    _sbWriter = new StringWriter(_sb);
                }
                return _sbWriter;
            }
        }

        /// <summary>
        /// Gets the debug message.
        /// </summary>
        public string DebugMessage {
            get {
                if(_sbWriter != null)
                    _sbWriter.Flush();
                return _sb.ToString();
            }
        }

        #endregion

        #region Application

        public void Apply(MainForm form) {
            Log.Write("Applying command line launch parameters");

            form.Opacity = (double)Opacity / 255.0;

            //Seek handle for thumbnail cloning
            WindowHandle handle = null;
            if (WindowId.HasValue) {
                handle = WindowHandle.FromHandle(WindowId.Value);
            }
            else if (WindowTitle != null) {
                var seeker = new ByTitleWindowSeeker(WindowTitle) {
                    OwnerHandle = form.Handle,
                    SkipNotVisibleWindows = MustBeVisible
                };
                seeker.Refresh();

                handle = seeker.Windows.FirstOrDefault();
            }
            else if (WindowClass != null) {
                var seeker = new ByClassWindowSeeker(WindowClass) {
                    OwnerHandle = form.Handle,
                    SkipNotVisibleWindows = MustBeVisible
                };
                seeker.Refresh();

                handle = seeker.Windows.FirstOrDefault();
            }

            if (StartPositionLock.HasValue) {
                form.PositionLock = StartPositionLock.Value;
            }

            //Clone any found handle (this applies thumbnail and aspect ratio)
            if (handle != null) {
                form.SetThumbnail(handle, Region);
            }

            //Adaptive size handling
            if (!StartSize.HasValue && (StartWidth.HasValue || StartHeight.HasValue)) {
                if (StartWidth.HasValue) {
                    StartSize = new Size(StartWidth.Value, form.ComputeHeightFromWidth(StartWidth.Value));
                }
                else {
                    StartSize = new Size(form.ComputeWidthFromHeight(StartHeight.Value), StartHeight.Value);
                }
            }

            //Size and location start values
            if (StartLocation.HasValue && StartSize.HasValue) {
                form.StartPosition = System.Windows.Forms.FormStartPosition.Manual;
                form.Location = StartLocation.Value;
                form.ClientSize = StartSize.Value;
            }
            else if (StartLocation.HasValue) {
                form.StartPosition = System.Windows.Forms.FormStartPosition.WindowsDefaultBounds;
                form.Location = StartLocation.Value;
            }
            else if (StartSize.HasValue) {
                form.StartPosition = System.Windows.Forms.FormStartPosition.WindowsDefaultLocation;
                form.ClientSize = StartSize.Value;
            }

            //Other features
            if (EnableClickForwarding) {
                form.ClickForwardingEnabled = true;
            }
            if (EnableClickThrough) {
                form.ClickThroughEnabled = true;
            }

            form.IsChromeVisible = !DisableChrome;

            //Fullscreen
            if (Fullscreen) {
                form.FullscreenManager.SwitchFullscreen();
            }
        }

        #endregion

        public static Options CreateFromForm(MainForm form) {
            var options = new Options();
            
            // Window Identity
            if (form.CurrentThumbnailWindowHandle != null) {
                options.WindowId = form.CurrentThumbnailWindowHandle.Handle;
            }
            // Note: We don't restore Title/Class here as we have the specific handle in the running instance.

            // Region
            // Requires internal access to ThumbnailPanel or a public property on MainForm
            // Assuming MainForm exposes 'ThumbnailPanel' or we use a workaround if checks fail.
            // For now, valid C# code assuming 'ThumbnailPanel' is accessible or we add it.
            // We'll add 'ThumbnailPanel' property to MainForm next.
            if (form.ThumbnailPanel != null && form.ThumbnailPanel.SelectedRegion != null) {
                options.Region = form.ThumbnailPanel.SelectedRegion;
            }

            // Visuals
            options.Opacity = (byte)(form.Opacity * 255);
            options.DisableChrome = !form.IsChromeVisible;
            options.EnableClickForwarding = form.ClickForwardingEnabled;
            options.EnableClickThrough = form.ClickThroughEnabled;
            options.Fullscreen = form.FullscreenManager.IsFullscreen;

            // Position (Lock vs Location)
            if (form.PositionLock.HasValue) {
                options.StartPositionLock = form.PositionLock;
            }
            else {
                // Offset slightly for the duplicate
                var loc = form.Location;
                loc.Offset(30, 30);
                options.StartLocation = loc;
                options.StartSize = form.ClientSize;
            }

            return options;
        }

        public string ToCommandLineArguments() {
            var sb = new StringBuilder();

            // Window ID
            if (WindowId.HasValue) {
                sb.AppendFormat("--windowId={0} ", WindowId.Value.ToInt64());
            }
            // Title/Class not typically used when cloning from ID, but supported if needed
            else if (!string.IsNullOrEmpty(WindowTitle)) {
                sb.AppendFormat("--windowTitle=\"{0}\" ", WindowTitle);
            }
            else if (!string.IsNullOrEmpty(WindowClass)) {
                sb.AppendFormat("--windowClass=\"{0}\" ", WindowClass);
            }

            // Region
            if (Region != null) {
                if (Region.Relative) {
                    var p = Region.BoundsAsPadding;
                    sb.AppendFormat("--padding={0},{1},{2},{3} ", p.Left, p.Top, p.Right, p.Bottom);
                }
                else {
                    var r = Region.Bounds;
                    sb.AppendFormat("--region={0},{1},{2},{3} ", r.X, r.Y, r.Width, r.Height);
                }
            }

            // Opacity
            sb.AppendFormat("--opacity={0} ", Opacity);

            // Flags
            if (DisableChrome) sb.Append("--chromeOff ");
            if (EnableClickForwarding) sb.Append("--clickForwarding ");
            if (EnableClickThrough) sb.Append("--clickThrough ");
            if (Fullscreen) sb.Append("--fullscreen ");
            if (MustBeVisible) sb.Append("--visible ");

            // Position
            if (StartPositionLock.HasValue) {
                sb.AppendFormat("--screenPosition={0} ", StartPositionLock.Value);
            }
            else {
                if (StartLocation.HasValue) {
                    sb.AppendFormat("--position={0},{1} ", StartLocation.Value.X, StartLocation.Value.Y);
                }
                if (StartSize.HasValue) {
                    sb.AppendFormat("--size={0},{1} ", StartSize.Value.Width, StartSize.Value.Height);
                }
            }
            
            // Width/Height individual args not strictly needed if Size is used, but good for completeness?
            // Size is preferred.

            return sb.ToString().Trim();
        }

    }

}
