#include <algorithm>
#include <cassert>
#include <sstream>
#include <streambuf>
#include <string>
#include <vessel/vessel.hpp>

namespace {

class FailingSource : public std::streambuf {
    std::streamsize xsgetn(char* buffer, std::streamsize count) override {
        if (sent_chunk_) {
            throw std::ios_base::failure("Source failed");
        }
        sent_chunk_ = true;
        std::fill_n(buffer, count, 'x');
        return count;
    }

    bool sent_chunk_ = false;
};

std::string read_object(vessel::Reader reader) {
    std::ostringstream output;
    reader.copy_to(output);
    return output.str();
}

} // namespace

int main() {
    const std::string key = "notes/" "\xC3\xA9" ".bin";
    const std::string payload("a\0b", 3);
    auto snapshot = [&] {
        vessel::Vessel store("smoke-store", {true});
        vessel::PutOptions options;
        options.overwrite = true;
        options.info = {"test.bin", ".bin", "application/octet-stream"};
        std::istringstream input(payload);
        input.exceptions(std::ios_base::failbit | std::ios_base::badbit);
        store.put(key, input, options);

        auto metadata = store.metadata(key);
        assert(metadata.key == key);
        assert(metadata.original_size == 3);
        assert(metadata.info.file_name == std::string("test.bin"));
        assert(metadata.creation_date_unix_ms > 0);
        assert(store.keys("notes/") == std::vector<std::string>{key});

        try {
            store.put(key, "duplicate");
            assert(false);
        } catch (const vessel::Error& error) {
            assert(error.status() == VESSEL_ERR_ALREADY_EXISTS);
        }

        auto reader = store.open_read(key);
        assert(reader.can_seek());
        assert(reader.length() == 3);
        reader.seek(-1, vessel::SeekOrigin::end);
        char byte = '\0';
        assert(reader.position() == 2);
        assert(reader.read(&byte, 1) == 1 && byte == 'b');
        reader.seek(0);

        const std::string compressed_data(128 * 1024, 'c');
        options.compression = vessel::Compression::zstd;
        store.put(key, compressed_data, options);

        FailingSource failing;
        std::istream broken(&failing);
        broken.exceptions(std::ios_base::badbit);
        try {
            store.put(key, broken, options);
            assert(false);
        } catch (const std::ios_base::failure&) {
        }
        auto compressed = store.open_read(key);
        assert(!compressed.can_seek());
        assert(compressed.metadata().compression == vessel::Compression::zstd);
        assert(read_object(std::move(compressed)) == compressed_data);
        assert(store.erase(key));
        assert(!store.erase(key));

        try {
            store.open_read(key);
            assert(false);
        } catch (const vessel::Error& error) {
            assert(error.status() == VESSEL_ERR_NOT_FOUND);
        }
        try {
            store.put(std::string_view("bad\0key", 7), "x");
            assert(false);
        } catch (const vessel::Error& error) {
            assert(error.status() == VESSEL_ERR_INVALID_ARGUMENT);
        }
        return reader;
    }();

    assert(snapshot.metadata().compression == vessel::Compression::none);
    assert(read_object(std::move(snapshot)) == payload);
}
