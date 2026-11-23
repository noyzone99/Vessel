fn main() {
    println!("cargo:rerun-if-env-changed=VESSEL_LIBRARY_DIR");
    if let Some(directory) = std::env::var_os("VESSEL_LIBRARY_DIR") {
        println!(
            "cargo:rustc-link-search=native={}",
            directory.to_string_lossy()
        );
    }
    println!("cargo:rustc-link-lib=dylib=vessel");
}
