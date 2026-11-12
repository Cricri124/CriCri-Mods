// Copyright (c) 2026 XtraCube

#include <utilities/library.h>
#include <utilities/tools.h>
#include <dlfcn.h>
#include <fcntl.h>
#include <link.h>
#include <sys/stat.h>
#include <unistd.h>
#include <algorithm>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <ctime>
#include <fstream>
#include <iterator>
#include <string>
#include <vector>
#include <logger.h>

#define TAG "LibraryUtils"

namespace {

    struct BiasLookup
    {
        const char *base_name;
        uintptr_t bias;
        int matches;
    };

// dlpi_addr is the load bias the loader chose for that library.
// Keeps the first match; callers that need uniqueness check `matches`.
    int find_bias(struct dl_phdr_info *info, size_t, void *data)
    {
        auto *r = static_cast<BiasLookup *>(data);
        if (!info->dlpi_name || !*info->dlpi_name) return 0;

        const char *slash = strrchr(info->dlpi_name, '/');
        if (strcmp(slash ? slash + 1 : info->dlpi_name, r->base_name) == 0)
        {
            if (r->matches == 0) r->bias = static_cast<uintptr_t>(info->dlpi_addr);
            r->matches++;
        }
        return 0;
    }

    // ---- CriCri Mods (11/11/2026, o CriCri: "tão falando que demora muito pra carregar") ----
    // padded_dlopen copiava a libil2cpp.so INTEIRA do jogo (mais de 100 MB) para a pasta do app a cada abertura, só para mudar um número
    // nos cabeçalhos. A cópia só precisa ser refeita quando a biblioteca do jogo muda (o jogo atualizou). As funções abaixo guardam, ao
    // lado da cópia, de qual arquivo ela saiu (caminho, tamanho, data, inode, tamanho do pool) e conferem a cópia antes de usar de novo.
    // A pasta é interna do app: nenhum outro app escreve nela. Qualquer dúvida (arquivo de origem diferente, cópia com outro tamanho ou
    // outros cabeçalhos, dlopen que falha) cai no caminho antigo: copiar tudo de novo.

    long long now_ms()
    {
        timespec ts{};
        clock_gettime(CLOCK_MONOTONIC, &ts);
        return static_cast<long long>(ts.tv_sec) * 1000LL + ts.tv_nsec / 1000000;
    }

    // Identity of the source library plus the pool size; empty if the source can't be inspected (then nothing is reused).
    std::string describe_source(const char *path, const struct stat &st, uint64_t pool_len)
    {
        std::string s = "1\n";
        s += path;
        s += '\n';
        s += std::to_string(static_cast<long long>(st.st_size));
        s += '\n';
        s += std::to_string(static_cast<long long>(st.st_mtim.tv_sec));
        s += '.';
        s += std::to_string(static_cast<long long>(st.st_mtim.tv_nsec));
        s += '\n';
        s += std::to_string(static_cast<unsigned long long>(st.st_ino));
        s += '\n';
        s += std::to_string(static_cast<unsigned long long>(pool_len));
        s += '\n';
        return s;
    }

    std::string read_small_file(const std::string &path)
    {
        std::ifstream f(path, std::ios::binary);
        if (!f) return {};
        return std::string((std::istreambuf_iterator<char>(f)), std::istreambuf_iterator<char>());
    }

    void write_small_file(const std::string &path, const std::string &content)
    {
        const std::string temp = path + ".tmp";
        {
            std::ofstream f(temp, std::ios::binary | std::ios::trunc);
            f << content;
            if (!f) return;
        }
        (void) rename(temp.c_str(), path.c_str());
    }

    // The existing copy has the source's size, the same ELF header and exactly the patched program headers.
    bool copy_matches(const char *copy, uint64_t source_size, const Elf_Ehdr &eh, const std::vector<Elf_Phdr> &phdrs)
    {
        struct stat st{};
        if (stat(copy, &st) != 0 || static_cast<uint64_t>(st.st_size) != source_size) return false;

        std::ifstream f(copy, std::ios::binary);
        Elf_Ehdr copied_eh{};
        if (!f.read(reinterpret_cast<char *>(&copied_eh), sizeof(copied_eh)) ||
            memcmp(&copied_eh, &eh, sizeof(eh)) != 0)
            return false;

        std::vector<Elf_Phdr> copied(phdrs.size());
        const auto bytes = static_cast<std::streamsize>(copied.size() * sizeof(Elf_Phdr));
        f.seekg(static_cast<std::streamoff>(eh.e_phoff));
        if (!f.read(reinterpret_cast<char *>(copied.data()), bytes)) return false;
        return memcmp(copied.data(), phdrs.data(), static_cast<size_t>(bytes)) == 0;
    }

    // Whole file, then the patched program headers on top; flushed to disk before anyone remembers it as good.
    bool write_padded_copy(const char *source, const char *target, const Elf_Ehdr &eh, const std::vector<Elf_Phdr> &phdrs)
    {
        int in = open(source, O_RDONLY | O_CLOEXEC);
        if (in < 0) return false;
        int out = open(target, O_WRONLY | O_CREAT | O_TRUNC | O_CLOEXEC, 0600);
        if (out < 0)
        {
            close(in);
            return false;
        }

        bool ok = true;
        std::vector<char> buffer(1024 * 1024);
        while (ok)
        {
            const ssize_t got = TEMP_FAILURE_RETRY(read(in, buffer.data(), buffer.size()));
            if (got == 0) break;
            if (got < 0)
            {
                ok = false;
                break;
            }
            ssize_t done = 0;
            while (done < got)
            {
                const ssize_t put = TEMP_FAILURE_RETRY(write(out, buffer.data() + done, static_cast<size_t>(got - done)));
                if (put <= 0)
                {
                    ok = false;
                    break;
                }
                done += put;
            }
        }

        const size_t header_bytes = phdrs.size() * sizeof(Elf_Phdr);
        if (ok && TEMP_FAILURE_RETRY(pwrite(out, phdrs.data(), header_bytes, static_cast<off_t>(eh.e_phoff))) !=
                  static_cast<ssize_t>(header_bytes))
            ok = false;
        if (ok && fsync(out) != 0) ok = false;
        if (close(out) != 0) ok = false;
        close(in);
        return ok;
    }

} // namespace

// Returns the load address of an already-loaded library (matched by file name,
// e.g. "libil2cpp.so" or a full path), or 0 if it isn't loaded.
uintptr_t get_module_base(const char *lib_name)
{
    if (!lib_name) return 0;

    const char *slash = strrchr(lib_name, '/');
    BiasLookup lookup{slash ? slash + 1 : lib_name, 0, 0};
    dl_iterate_phdr(find_bias, &lookup);
    return lookup.matches > 0 ? lookup.bias : 0;
}

// Copies library_name to temp_path with its last PT_LOAD segment extended by a
// page-aligned, zero-filled (bss) pool, then dlopens the copy.
PaddedOpenResult padded_dlopen(const char *library_name,
                               const char *temp_path,
                               size_t pool_size)
{
    const PaddedOpenResult failure{nullptr, nullptr, 0, 0};

    const Elf_Addr page = static_cast<Elf_Addr>(sysconf(_SC_PAGESIZE));
    auto align_up = [page](Elf_Addr v) { return (v + page - 1) & ~(page - 1); };

    // read ELF + program headers
    std::ifstream in(library_name, std::ios::binary);
    Elf_Ehdr eh{};
    if (!in.read(reinterpret_cast<char *>(&eh), sizeof(eh)) ||
        memcmp(eh.e_ident, ELFMAG, SELFMAG) != 0)
    {
        log_format(LogLevel::ERROR, TAG, "Failed to read ELF header of {}", library_name);
        return failure;
    }

    std::vector<Elf_Phdr> phdrs(eh.e_phnum);
    in.seekg(static_cast<std::streamoff>(eh.e_phoff));
    if (!in.read(reinterpret_cast<char *>(phdrs.data()), phdrs.size() * sizeof(Elf_Phdr)))
    {
        log_format(LogLevel::ERROR, TAG, "Failed to read program headers of {}", library_name);
        return failure;
    }

    // find the highest-ending PT_LOAD
    Elf_Phdr *last = nullptr;
    for (auto &ph : phdrs)
    {
        if (ph.p_type != PT_LOAD) continue;
        if (!last || ph.p_vaddr + ph.p_memsz > last->p_vaddr + last->p_memsz) last = &ph;
    }
    if (!last || !(last->p_flags & PF_W))
    {
        log_format(LogLevel::ERROR, TAG, "No writable last PT_LOAD in {}", library_name);
        return failure;
    }

    // pool = whole pages right after the segment; grow memsz to cover it
    const Elf_Addr pool_start = align_up(last->p_vaddr + last->p_memsz);
    const Elf_Addr pool_len = align_up(pool_size);
    last->p_memsz = pool_start + pool_len - last->p_vaddr;

    in.close();

    // CriCri Mods: the patched copy from the last launch is used again while the game's library is the same file (see the top of this file).
    const std::string origin_path = std::string(temp_path) + ".origem";
    std::string origin;
    struct stat source_stat{};
    if (stat(library_name, &source_stat) == 0)
    {
        origin = describe_source(library_name, source_stat, static_cast<uint64_t>(pool_len));
    }

    void *handle = nullptr;
    const long long started = now_ms();
    if (!origin.empty() && read_small_file(origin_path) == origin &&
        copy_matches(temp_path, static_cast<uint64_t>(source_stat.st_size), eh, phdrs))
    {
        handle = dlopen(temp_path, RTLD_GLOBAL | RTLD_NOW);
        if (handle)
        {
            log_format(LogLevel::INFO, TAG, "Reused the padded copy {} (checked and loaded in {} ms)",
                       temp_path, now_ms() - started);
        }
        else
        {
            log_format(LogLevel::WARN, TAG, "The kept copy {} did not load ({}): copying again",
                       temp_path, safe_dlerror());
        }
    }

    if (!handle)
    {
        // write the patched copy: whole file, then overwrite the program headers
        unlink(origin_path.c_str());
        if (!write_padded_copy(library_name, temp_path, eh, phdrs))
        {
            log_format(LogLevel::ERROR, TAG, "Failed to write {}", temp_path);
            return failure;
        }
        const long long copied = now_ms();

        handle = dlopen(temp_path, RTLD_GLOBAL | RTLD_NOW);
        if (!handle)
        {
            log_format(LogLevel::ERROR, TAG, "dlopen failed for {}: {}", temp_path, safe_dlerror());
            return failure;
        }
        log_format(LogLevel::INFO, TAG, "Wrote the padded copy {} in {} ms (kept for the next launches)",
                   temp_path, copied - started);
        // only now, with the copy on disk and proven loadable, is it remembered as good
        if (!origin.empty()) write_small_file(origin_path, origin);
    }

    // ask the loader where it put the library (no symbol needed)
    const char *slash = strrchr(temp_path, '/');
    BiasLookup lookup{slash ? slash + 1 : temp_path, 0, 0};
    dl_iterate_phdr(find_bias, &lookup);
    if (lookup.matches != 1)
    {
        log_format(LogLevel::ERROR, TAG, "Expected one loaded copy of {}, found {}",
                   temp_path, lookup.matches);
        dlclose(handle);
        return failure;
    }
    const uintptr_t load_bias = lookup.bias;
    const uintptr_t pool_addr = load_bias + pool_start;

    log_format(LogLevel::INFO, TAG, "Trampoline pool [0x{:X}, 0x{:X}) ready in {}",
               pool_addr, pool_addr + pool_len, temp_path);
    return {handle, reinterpret_cast<void *>(load_bias), pool_addr, static_cast<size_t>(pool_len)};
}