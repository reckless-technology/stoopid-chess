using SDL;
using StoopidChess.Desktop.Views;

namespace StoopidChess.Desktop.Sdl;

/// <summary>
/// SDL's key codes as the views' keys. Excluded from the coverage gate with the rest of the SDL host.
/// </summary>
internal static class SdlKeys
{
    public static Key KeyOf(SDL_Keycode key)
    {
        if (key >= SDL_Keycode.SDLK_A && key <= SDL_Keycode.SDLK_Z)
        {
            return Key.A + (int)(key - SDL_Keycode.SDLK_A);
        }
        return key switch
        {
            SDL_Keycode.SDLK_RETURN or SDL_Keycode.SDLK_KP_ENTER or SDL_Keycode.SDLK_RETURN2 => Key.Return,
            SDL_Keycode.SDLK_ESCAPE => Key.Escape,
            SDL_Keycode.SDLK_TAB => Key.Tab,
            SDL_Keycode.SDLK_BACKSPACE => Key.Backspace,
            SDL_Keycode.SDLK_DELETE => Key.Delete,
            SDL_Keycode.SDLK_LEFT => Key.Left,
            SDL_Keycode.SDLK_RIGHT => Key.Right,
            SDL_Keycode.SDLK_UP => Key.Up,
            SDL_Keycode.SDLK_DOWN => Key.Down,
            SDL_Keycode.SDLK_HOME => Key.Home,
            SDL_Keycode.SDLK_END => Key.End,
            SDL_Keycode.SDLK_PAGEUP => Key.PageUp,
            SDL_Keycode.SDLK_PAGEDOWN => Key.PageDown,
            _ => Key.Other,
        };
    }
}
