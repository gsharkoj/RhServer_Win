using System;
using System.Collections.Generic;
using System.Text;

namespace RhServer
{
    public enum Command
    {
        set_size = 100,
        set_image = 101,
        read_image = 102,
        image_update = 103,
        set_mouse = 104,
        mouse_update = 105,
        echo = 106,
        echo_ok = 107,
        get_image = 108,
        get_id = 109,
        set_id = 110,
        get_connect = 111,
        set_connect = 112,
        get_size = 113,
        get_stop = 114,
        set_stop = 115,
        data_fail = 116,
        key_mouse_update = 117,
        set_clipboard_data = 118,
        set_image_size = 119,
        file_command = 120,
        ping = 121,
    }
    public enum MouseCommand
    {
        move = 100,
        click = 101,
        dclick = 102,
        mouse_down = 103,
        mouse_up = 104,
        key_down = 105,
        key_up = 106,
        key_press = 107,
        mouse_wheel = 108,
    }
    public enum ResultConnection
    {
        ok = 100,
        negative = 101,
        not_found = 102,
    }
}
