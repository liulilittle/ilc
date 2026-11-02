#include <cstdint>

// sbprobe.cpp -- native fixture for tests/cases/pinvoke-stringbuilder.
//
// The callee receives the StringBuilder's OWN buffer and writes through it.
// The net48 marshaller hands over Capacity UTF-16 units (not Length) filled
// with the current content + a NUL terminator at index Length, and recomputes
// Length with strnlen(buffer, Capacity) after the call. Every helper below
// returns what it wrote so the managed records pin both directions.
extern "C" {

static int32_t sb_len(const char16_t* text)
{
    int32_t n = 0;
    while (text[n] != 0)
    {
        ++n;
    }
    return n;
}

// Write 'text' + NUL into the StringBuilder buffer.
int32_t sb_write_terminated(char16_t* buffer, const char16_t* text)
{
    const int32_t n = sb_len(text);
    for (int32_t i = 0; i < n; ++i)
    {
        buffer[i] = text[i];
    }
    buffer[n] = 0;
    return n + 1;
}

// Write exactly the text's units, NO terminator.
int32_t sb_write_raw(char16_t* buffer, const char16_t* text)
{
    const int32_t n = sb_len(text);
    for (int32_t i = 0; i < n; ++i)
    {
        buffer[i] = text[i];
    }
    return n;
}

// GetWindowText shape: a caller-sized buffer, a NUL-terminated write.
// GC contract: the callee re-enters managed code (the delegate forces a
// compacting collection) and THEN writes through the buffer pointer it was
// handed. If the builder's chunk were relocated by that collection, the write
// would land in dead memory and the managed readback below would differ.
int32_t sb_gc_poke(char16_t* buffer, void (*poke)())
{
    if (poke != nullptr)
    {
        poke();
    }
    static const char16_t kText[] = u"AfterGC";
    int32_t n = sb_len(kText);
    for (int32_t i = 0; i < n; ++i)
    {
        buffer[i] = kText[i];
    }
    buffer[n] = 0;
    return n;
}
int32_t sb_get_window_text(char16_t* buffer)
{
    static const char16_t kText[] = u"Hello Window";
    const int32_t n = sb_len(kText);
    for (int32_t i = 0; i < n; ++i)
    {
        buffer[i] = kText[i];
    }
    buffer[n] = 0;
    return n;
}

// Read one unit of the native view.
int32_t sb_peek(char16_t* buffer, int32_t index)
{
    return static_cast<int32_t>(buffer[index]);
}

} // extern "C"
