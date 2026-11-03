// tauri sürümü vakıf: main burada, gerçek iş lib.rs'te
#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

fn main() {
    kickforge_desktop_lib::run()
}
