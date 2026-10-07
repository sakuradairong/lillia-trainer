#pragma once
#include <algorithm>
#include <cmath>
#include <cstdint>
#include <vector>
#include <stdexcept>
#include <string>

namespace outfit {
inline int mask(const std::wstring& text) {
    int result=0;size_t start=0;
    do {
        size_t end=text.find(L'|',start);std::wstring part=text.substr(start,end-start);
        int bit=0;
        if(part==L"Accessory1")bit=1;else if(part==L"Accessory2")bit=2;
        else if(part==L"Shoes")bit=4;else if(part==L"Panties")bit=8;else if(part==L"Bra")bit=16;
        else if(part==L"UnderDress")bit=32;else if(part==L"UpperDress")bit=64;
        else if(part==L"Stockings")bit=128;else if(part==L"Arm")bit=256;
        else if(part==L"HeadAccessory1")bit=512;else if(part==L"Hat")bit=2048;else if(part==L"Hair")bit=4096;
        else return 0;
        result|=bit;
        if(end==std::wstring::npos)break;start=end+1;
    } while(start<text.size());
    return result;
}
struct Rect { int x, y, w, h; };
inline bool gridBounds(int width, int height) {
    if (width < 1 || height < 1) return false;
    // Keep existing tall grids and accept every size allowed by WarehouseCapacity.
    return (width <= 100 && height <= 2000)
        || (width <= 256 && height <= 256 && static_cast<int64_t>(width) * height <= 4096);
}
// GetInventorySize returns UnityEngine.Vector2: validate its floats before conversion.
inline bool gridSize(float width, float height, int& w, int& h) {
    if (!std::isfinite(width) || !std::isfinite(height)
        || width < 1.0f || width > 256.0f || height < 1.0f || height > 2000.0f
        || std::floor(width) != width || std::floor(height) != height) return false;
    int columns = static_cast<int>(width), rows = static_cast<int>(height);
    if (!gridBounds(columns, rows)) return false;
    w = columns; h = rows;
    return true;
}
inline bool conflicts(int worn,int incoming) { return (worn&4096)==0 && (worn&incoming)!=0; }
inline bool overlaps(const Rect& a, const Rect& b) {
    return a.x < b.x + b.w && b.x < a.x + a.w && a.y < b.y + b.h && b.y < a.y + a.h;
}
inline bool valid(const Rect& r, int w, int h) {
    return r.w > 0 && r.h > 0 && r.x >= 0 && r.y >= 0 && r.w <= w && r.h <= h
        && r.x <= w-r.w && r.y <= h-r.h;
}
inline bool place(int w, int h, const std::vector<Rect>& occupied,
                  const std::vector<Rect>& incoming, std::vector<Rect>& result) {
    result.clear();
    if (!gridBounds(w, h)) return false;
    std::vector<Rect> used = occupied;
    for (size_t i=0;i<used.size();++i) {
        if (!valid(used[i],w,h)) return false;
        for (size_t j=0;j<i;++j) if (overlaps(used[i],used[j])) return false;
    }
    for (auto r : incoming) {
        bool found=false;
        if (r.w <= 0 || r.h <= 0 || r.w>w || r.h>h) { result.clear(); return false; }
        for (int y=0;y<=h-r.h && !found;++y) for (int x=0;x<=w-r.w;++x) {
            r.x=x;r.y=y;
            if (std::none_of(used.begin(),used.end(),[&](const Rect& q){return overlaps(r,q);})) {
                used.push_back(r);result.push_back(r);found=true;break;
            }
        }
        if (!found) { result.clear(); return false; }
    }
    return true;
}

// Fixed-width wire format shared with the x64 C# client. No game addresses are persisted.
struct Request {
    uint32_t magic, version, count, result;
    uint64_t holder, save;
    int32_t preset, applied;
    int32_t ids[16];
    uint64_t items[16];
    char message[512];
};
static_assert(sizeof(Request)==744,"outfit request ABI");
inline bool requestValid(const Request& r) {
    return r.magic==0x4C4C4F46 && r.version==1 && r.count>0 && r.count<=16
        && r.preset>=0 && r.preset<4 && r.holder>=0x10000 && r.save>=0x10000;
}
}
