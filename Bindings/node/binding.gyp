{
  "targets": [
    {
      "target_name": "vessel",
      "sources": ["native/addon.cc"],
      "defines": ["NAPI_VERSION=8"],
      "cflags_cc!": ["-fno-exceptions"],
      "cflags_cc": ["-std=c++20"],
      "xcode_settings": {
        "CLANG_CXX_LANGUAGE_STANDARD": "c++20",
        "GCC_ENABLE_CPP_EXCEPTIONS": "YES"
      },
      "msvs_settings": {
        "VCCLCompilerTool": {
          "ExceptionHandling": 1,
          "AdditionalOptions": ["/std:c++20"]
        }
      },
      "conditions": [["OS=='linux'", { "libraries": ["-ldl"] }]]
    }
  ]
}
