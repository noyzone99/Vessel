#include <iostream>
#include <sstream>
#include <vessel/vessel.hpp>

int main() {
    vessel::Vessel store("my-store");
    const auto* key = "documents/hello.txt";
    std::istringstream input("Hello from Vessel!");
    vessel::PutOptions options;
    options.overwrite = true;
    store.put(key, input, options);
    auto reader = store.open_read(key);
    reader.copy_to(std::cout);
    std::cout << '\n';
}
