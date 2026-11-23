#ifndef VESSEL_CPP_VESSEL_HPP
#define VESSEL_CPP_VESSEL_HPP

#include <algorithm>
#include <array>
#include <cstddef>
#include <cstdint>
#include <exception>
#include <istream>
#include <limits>
#include <memory>
#include <optional>
#include <ostream>
#include <stdexcept>
#include <string>
#include <string_view>
#include <utility>
#include <vector>

#include <vessel.h>

namespace vessel {

class Error : public std::runtime_error {
public:
    Error(const char* operation, vessel_error_t status)
        : std::runtime_error(std::string(operation) + " (Vessel status " + std::to_string(status) + ")"),
          status_(status) {}

    vessel_error_t status() const noexcept { return status_; }

private:
    vessel_error_t status_;
};

enum class Compression {
    none = VESSEL_COMPRESSION_NONE,
    zstd = VESSEL_COMPRESSION_ZSTD,
    lz4 = VESSEL_COMPRESSION_LZ4
};

enum class CompressionLevel {
    fastest = VESSEL_LEVEL_FASTEST,
    balanced = VESSEL_LEVEL_BALANCED,
    high = VESSEL_LEVEL_HIGH,
    ultra = VESSEL_LEVEL_ULTRA
};

enum class SeekOrigin {
    begin = VESSEL_SEEK_SET,
    current = VESSEL_SEEK_CUR,
    end = VESSEL_SEEK_END
};

struct Options {
    bool metadata_compression = false;
};

struct ObjectInfo {
    std::optional<std::string> file_name;
    std::optional<std::string> extension;
    std::optional<std::string> mime_type;
};

struct PutOptions {
    bool overwrite = false;
    Compression compression = Compression::none;
    CompressionLevel level = CompressionLevel::balanced;
    ObjectInfo info;
};

struct Metadata {
    std::string key;
    std::string hash;
    std::string etag;
    std::int64_t original_size;
    std::int64_t compressed_size;
    Compression compression;
    std::int64_t creation_date_unix_ms;
    ObjectInfo info;
};

namespace detail {

inline void check(vessel_error_t status, const char* operation) {
    if (status != VESSEL_SUCCESS) {
        throw Error(operation, status);
    }
}

inline void check_text(std::string_view text) {
    if (text.find('\0') != std::string_view::npos) {
        throw Error("UTF-8 argument contains an embedded NUL", VESSEL_ERR_INVALID_ARGUMENT);
    }
}

inline std::string text_argument(std::string_view text) {
    check_text(text);
    return std::string(text);
}

inline const char* optional_argument(const std::optional<std::string>& text) {
    if (!text) {
        return nullptr;
    }
    check_text(*text);
    return text->c_str();
}

inline vessel_object_info_t native_info(const ObjectInfo& info) {
    return {optional_argument(info.file_name), optional_argument(info.extension), optional_argument(info.mime_type)};
}

inline std::optional<std::string> optional_text(const char* text) {
    if (text) {
        return std::string(text);
    }
    return std::nullopt;
}

struct NativeMetadata {
    vessel_metadata_result_t result{};

    NativeMetadata() = default;
    NativeMetadata(const NativeMetadata&) = delete;
    NativeMetadata& operator=(const NativeMetadata&) = delete;
    ~NativeMetadata() { vessel_free_metadata(&result.metadata); }

    Metadata copy() const {
        const auto& value = result.metadata;
        return {value.key, value.hash, value.etag, value.original_size, value.compressed_size,
            static_cast<Compression>(value.compression_type), result.creation_date_unix_ms,
            {optional_text(value.file_name), optional_text(value.extension), optional_text(value.mime_type)}};
    }
};

struct Upload {
    std::istream& source;
    std::exception_ptr error;
};

inline std::int32_t VESSEL_CALL read_upload(void* context, std::uint8_t* buffer, std::int32_t capacity) noexcept {
    auto& upload = *static_cast<Upload*>(context);
    auto& source = upload.source;
    try {
        if (source.eof() && !source.bad()) {
            return 0;
        }
        try {
            source.read(reinterpret_cast<char*>(buffer), capacity);
        } catch (const std::ios_base::failure&) {
            if (!source.eof() || source.bad()) {
                throw;
            }
        }
        if (source.bad() || (source.fail() && !source.eof())) {
            throw std::ios_base::failure("Vessel upload source failed");
        }
        return static_cast<std::int32_t>(source.gcount());
    } catch (...) {
        upload.error = std::current_exception();
        return VESSEL_ERR_IO;
    }
}

struct KeyList {
    std::vector<std::string> keys;
    std::exception_ptr error;
};

inline std::int32_t VESSEL_CALL append_key(void* context, const char* key) noexcept {
    auto& result = *static_cast<KeyList*>(context);
    try {
        result.keys.emplace_back(key);
        return 0;
    } catch (...) {
        result.error = std::current_exception();
        return VESSEL_ERR_CANCELLED;
    }
}

} // namespace detail

class Reader {
public:
    Reader(Reader&&) noexcept = default;
    Reader& operator=(Reader&&) noexcept = default;

    std::size_t read(char* buffer, std::size_t size) {
        auto count = (std::min)(size, static_cast<std::size_t>((std::numeric_limits<std::int32_t>::max)()));
        std::int32_t bytes_read = 0;
        detail::check(vessel_stream_read(handle_.get(), reinterpret_cast<std::uint8_t*>(buffer),
            static_cast<std::int32_t>(count), &bytes_read), "Read object");
        return static_cast<std::size_t>(bytes_read);
    }

    void copy_to(std::ostream& output) {
        std::array<char, 64 * 1024> buffer;
        while (auto count = read(buffer.data(), buffer.size())) {
            if (!output.write(buffer.data(), static_cast<std::streamsize>(count))) {
                throw std::ios_base::failure("Vessel download destination failed");
            }
        }
    }

    std::int64_t length() const {
        std::int64_t result = 0;
        detail::check(vessel_stream_length(handle_.get(), &result), "Get object length");
        return result;
    }

    Metadata metadata() const {
        detail::NativeMetadata result;
        detail::check(vessel_stream_get_metadata(handle_.get(), &result.result), "Get stream metadata");
        return result.copy();
    }

    bool can_seek() const {
        std::int32_t result = 0;
        detail::check(vessel_stream_can_seek(handle_.get(), &result), "Check whether object is seekable");
        return result != 0;
    }

    std::int64_t position() const {
        std::int64_t result = 0;
        detail::check(vessel_stream_position(handle_.get(), &result), "Get read position");
        return result;
    }

    void seek(std::int64_t offset, SeekOrigin origin = SeekOrigin::begin) {
        detail::check(vessel_stream_seek(handle_.get(), offset, static_cast<std::int32_t>(origin)), "Seek object");
    }

private:
    friend class Vessel;

    explicit Reader(vessel_stream_t handle) noexcept : handle_(handle, vessel_stream_close) {}

    std::unique_ptr<void, decltype(&vessel_stream_close)> handle_;
};

class Vessel {
public:
    explicit Vessel(std::string_view path, const Options& options = {}) {
        if (vessel_abi_version() != VESSEL_ABI_VERSION) {
            throw Error("Incompatible Vessel ABI", VESSEL_ERR_INVALID_OPERATION);
        }
        auto name = detail::text_argument(path);
        const vessel_options_t config{options.metadata_compression, nullptr, nullptr};
        vessel_t handle = nullptr;
        detail::check(vessel_init_ex(name.c_str(), &config, &handle), "Open store");
        handle_.reset(handle);
    }

    Vessel(Vessel&&) noexcept = default;
    Vessel& operator=(Vessel&&) noexcept = default;

    void put(std::string_view key, std::istream& source, const PutOptions& options = {}) const {
        auto name = detail::text_argument(key);
        detail::Upload upload{source, {}};
        auto status = vessel_put_stream(handle_.get(), name.c_str(), detail::read_upload, &upload,
            options.overwrite, detail::native_info(options.info), static_cast<std::int32_t>(options.compression),
            static_cast<std::int32_t>(options.level), nullptr);
        if (upload.error) {
            std::rethrow_exception(upload.error);
        }
        detail::check(status, "Write object");
    }

    void put(std::string_view key, std::string_view bytes, const PutOptions& options = {}) const {
        if (bytes.size() > static_cast<std::uint64_t>((std::numeric_limits<std::int64_t>::max)())) {
            throw Error("Object size exceeds the ABI range", VESSEL_ERR_INVALID_ARGUMENT);
        }
        auto name = detail::text_argument(key);
        detail::check(vessel_put_ex(handle_.get(), name.c_str(), reinterpret_cast<const std::uint8_t*>(bytes.data()),
            static_cast<std::int64_t>(bytes.size()), options.overwrite, detail::native_info(options.info),
            static_cast<std::int32_t>(options.compression), static_cast<std::int32_t>(options.level), nullptr),
            "Write object");
    }

    Reader open_read(std::string_view key) const {
        auto name = detail::text_argument(key);
        vessel_stream_t result = nullptr;
        detail::check(vessel_stream_open(handle_.get(), name.c_str(), &result), "Open object stream");
        return Reader(result);
    }

    Metadata metadata(std::string_view key) const {
        auto name = detail::text_argument(key);
        detail::NativeMetadata result;
        detail::check(vessel_get_metadata_ex(handle_.get(), name.c_str(), &result.result), "Get object metadata");
        return result.copy();
    }

    std::vector<std::string> keys(std::string_view prefix = {}) const {
        auto name = detail::text_argument(prefix);
        detail::KeyList result;
        auto status = vessel_get_all_keys_ex(handle_.get(), name.c_str(), detail::append_key, &result);
        if (result.error) {
            std::rethrow_exception(result.error);
        }
        detail::check(status, "List keys");
        return std::move(result.keys);
    }

    bool erase(std::string_view key) const {
        auto name = detail::text_argument(key);
        std::int32_t deleted = 0;
        detail::check(vessel_delete(handle_.get(), name.c_str(), &deleted), "Delete object");
        return deleted != 0;
    }

private:
    std::unique_ptr<void, decltype(&vessel_dispose)> handle_{nullptr, vessel_dispose};
};

} // namespace vessel

#endif
