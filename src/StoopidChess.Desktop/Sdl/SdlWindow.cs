using SDL;
using SkiaSharp;
using StoopidChess.Desktop.Views;

namespace StoopidChess.Desktop.Sdl;

/// <summary>
/// One SDL window showing a <see cref="ViewRoot"/>: the views draw with Skia into a raster bitmap at the window's
/// physical pixels, which an SDL streaming texture presents (no GL context of the app's own). The root works in
/// logical pixels, the physical ones over the window's display scale, so the window is the same size to the eye on
/// every display. Excluded from the coverage gate with the rest of the SDL host: it holds no logic, only calls.
/// </summary>
internal sealed unsafe class SdlWindow : IDisposable
{
    private SDL_Renderer* _renderer;
    private SDL_Texture* _texture;
    private SKBitmap? _bitmap;
    private bool _isTextInputOn;

    public SdlWindow(string title, SKSizeI size, SKSizeI minimumSize, ViewRoot root)
    {
        Root = root;
        float contentScale = SDL3.SDL_GetDisplayContentScale(SDL3.SDL_GetPrimaryDisplay());
        if (contentScale <= 0)
        {
            contentScale = 1;
        }
        Handle = SDL3.SDL_CreateWindow(
            title,
            (int)(size.Width * contentScale),
            (int)(size.Height * contentScale),
            SDL_WindowFlags.SDL_WINDOW_RESIZABLE
                | SDL_WindowFlags.SDL_WINDOW_HIGH_PIXEL_DENSITY
                | SDL_WindowFlags.SDL_WINDOW_HIDDEN
        );
        if (Handle == null)
        {
            throw new InvalidOperationException($"SDL could not open a window: {SDL3.SDL_GetError()}");
        }
        SDL3.SDL_SetWindowMinimumSize(
            Handle,
            (int)(minimumSize.Width * contentScale),
            (int)(minimumSize.Height * contentScale)
        );
        _renderer = SDL3.SDL_CreateRenderer(Handle, (string?)null);
        if (_renderer == null)
        {
            _renderer = SDL3.SDL_CreateRenderer(Handle, "software");
        }
        if (_renderer == null)
        {
            throw new InvalidOperationException($"SDL could not make a renderer: {SDL3.SDL_GetError()}");
        }
        Id = SDL3.SDL_GetWindowID(Handle);
        SetIcon();
        root.Invalidate();
    }

    /// <summary>The SDL window.</summary>
    public SDL_Window* Handle { get; private set; }

    /// <summary>The SDL window's id, which its events carry.</summary>
    public SDL_WindowID Id { get; }

    /// <summary>The views shown.</summary>
    public ViewRoot Root { get; }

    /// <summary>Physical pixels per logical pixel.</summary>
    public float Scale { get; private set; } = 1;

    /// <summary>Logical pixels per SDL window coordinate (the mouse's).</summary>
    private float PointScale => SDL3.SDL_GetWindowPixelDensity(Handle) / Scale;

    public void Show() => SDL3.SDL_ShowWindow(Handle);

    public void SetTitle(string title) => SDL3.SDL_SetWindowTitle(Handle, title);

    public void Raise() => SDL3.SDL_RaiseWindow(Handle);

    /// <summary>A point in SDL's window coordinates, in the root's logical pixels.</summary>
    public SKPoint ToLogical(float x, float y) => new(x * PointScale, y * PointScale);

    /// <summary>
    /// Draw the views, if anything has changed or <paramref name="isForced"/>, and present them.
    /// </summary>
    public void Render(bool isForced = false)
    {
        int width;
        int height;
        SDL3.SDL_GetWindowSizeInPixels(Handle, &width, &height);
        if (width <= 0 || height <= 0)
        {
            return;
        }
        float scale = SDL3.SDL_GetWindowDisplayScale(Handle);
        scale = scale > 0 ? scale : 1;
        bool isNewSize = _bitmap is null || _bitmap.Width != width || _bitmap.Height != height || scale != Scale;
        if (!isNewSize && !isForced && !Root.IsDirty)
        {
            return;
        }
        if (isNewSize)
        {
            Scale = scale;
            _bitmap?.Dispose();
            _bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
            if (_texture != null)
            {
                SDL3.SDL_DestroyTexture(_texture);
            }
            _texture = SDL3.SDL_CreateTexture(
                _renderer,
                SDL_PixelFormat.SDL_PIXELFORMAT_ARGB8888,
                SDL_TextureAccess.SDL_TEXTUREACCESS_STREAMING,
                width,
                height
            );
            Root.Resize(width / scale, height / scale);
        }
        using (var canvas = new SKCanvas(_bitmap))
        {
            canvas.Scale(Scale);
            Root.Render(canvas);
        }
        SDL3.SDL_UpdateTexture(_texture, null, _bitmap!.GetPixels(), _bitmap.RowBytes);
        SDL3.SDL_RenderTexture(_renderer, _texture, null, null);
        SDL3.SDL_RenderPresent(_renderer);
    }

    /// <summary>
    /// Text input on while a view that takes typing holds the keyboard, off otherwise.
    /// </summary>
    public void UpdateTextInput()
    {
        bool wants = Root.WantsTextInput;
        if (wants == _isTextInputOn)
        {
            return;
        }
        _isTextInputOn = wants;
        if (wants)
        {
            SDL3.SDL_StartTextInput(Handle);
        }
        else
        {
            SDL3.SDL_StopTextInput(Handle);
        }
    }

    /// <summary>
    /// The window's icon: the 256 px picture tools/make-icons.py draws.
    /// </summary>
    private void SetIcon()
    {
        using SKImage image = Look.Assets.Image("AppIcon.png");
        using var icon = new SKBitmap(
            new SKImageInfo(image.Width, image.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul)
        );
        if (!image.ReadPixels(icon.Info, icon.GetPixels(), icon.RowBytes, 0, 0))
        {
            return;
        }
        SDL_Surface* surface = SDL3.SDL_CreateSurfaceFrom(
            icon.Width,
            icon.Height,
            SDL_PixelFormat.SDL_PIXELFORMAT_ARGB8888,
            icon.GetPixels(),
            icon.RowBytes
        );
        if (surface != null)
        {
            SDL3.SDL_SetWindowIcon(Handle, surface);
            SDL3.SDL_DestroySurface(surface);
        }
    }

    public void Dispose()
    {
        if (_texture != null)
        {
            SDL3.SDL_DestroyTexture(_texture);
            _texture = null;
        }
        if (_renderer != null)
        {
            SDL3.SDL_DestroyRenderer(_renderer);
            _renderer = null;
        }
        if (Handle != null)
        {
            SDL3.SDL_DestroyWindow(Handle);
            Handle = null;
        }
        _bitmap?.Dispose();
        _bitmap = null;
    }
}
