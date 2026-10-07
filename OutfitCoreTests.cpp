#include "OutfitCore.h"
#include <cstdio>
#include <limits>
int main() {
    int tests=0,fails=0;
    auto check=[&](const char* n,bool b){++tests;if(!b){++fails;std::printf("FAIL %s\n",n);}};
    using outfit::Rect;std::vector<Rect> out;
    int width=0,height=0;
    check("Vector2 storage size supports placement",outfit::gridSize(8.0f,40.0f,width,height)
        && width==8 && height==40 && outfit::place(width,height,{{0,0,8,2}},{{0,0,2,2}},out)
        && out.size()==1 && out[0].y==2);
    check("maximum storage dimensions accepted",outfit::gridSize(100.0f,2000.0f,width,height)
        && width==100 && height==2000);
    const float nan=std::numeric_limits<float>::quiet_NaN();
    const float inf=std::numeric_limits<float>::infinity();
    const float huge=std::numeric_limits<float>::max();
    check("NaN storage dimensions rejected",!outfit::gridSize(nan,40.0f,width,height)
        && !outfit::gridSize(8.0f,nan,width,height));
    check("infinite storage dimensions rejected",!outfit::gridSize(inf,40.0f,width,height)
        && !outfit::gridSize(8.0f,inf,width,height));
    check("negative storage dimensions rejected",!outfit::gridSize(-8.0f,40.0f,width,height)
        && !outfit::gridSize(8.0f,-40.0f,width,height));
    check("zero storage dimensions rejected",!outfit::gridSize(0.0f,40.0f,width,height)
        && !outfit::gridSize(8.0f,0.0f,width,height));
    check("fractional storage dimensions rejected",!outfit::gridSize(8.5f,40.0f,width,height)
        && !outfit::gridSize(8.0f,40.5f,width,height));
    check("storage dimensions exceeding planner bounds rejected",!outfit::gridSize(257.0f,40.0f,width,height)
        && !outfit::gridSize(8.0f,2001.0f,width,height));
    check("huge floats rejected before integer conversion",!outfit::gridSize(huge,40.0f,width,height)
        && !outfit::gridSize(8.0f,huge,width,height));
    check("invalid dimensions preserve prior output",width==100 && height==2000);
    check("expanded wide warehouse supports outfit placement",outfit::gridSize(256.0f,16.0f,width,height)
        && width==256 && height==16 && outfit::place(width,height,{},{{0,0,2,2}},out) && out.size()==1);
    check("wide warehouse exceeding expansion limit rejected",!outfit::gridSize(256.0f,17.0f,width,height)
        && !outfit::place(256,17,{},{{0,0,2,2}},out) && out.empty());
    check("combined equip slots",outfit::mask(L"Bra|UpperDress")==80);
    check("unknown equip slot rejected",outfit::mask(L"Bra|Unknown")==0);
    check("hair bit preserved",(outfit::mask(L"Hair|Hat")&4096)!=0);
    check("uncovered outfit slots retained",!outfit::conflicts(2048,128));
    check("composite conflict handled",outfit::conflicts(80,64)&&!outfit::conflicts(6144,2048));
    check("empty grid",outfit::place(4,4,{},{{0,0,2,2},{0,0,2,2}},out)&&out.size()==2&&!outfit::overlaps(out[0],out[1]));
    check("reserved cells",outfit::place(4,4,{{0,0,4,2}},{{0,0,2,2}},out)&&out[0].y==2);
    check("full storage rejected",!outfit::place(2,2,{{0,0,2,2}},{{0,0,1,1}},out)&&out.empty());
    check("oversized item rejected",!outfit::place(2,2,{},{{0,0,3,1}},out));
    check("invalid existing item rejected",!outfit::place(4,4,{{-1,0,1,1}},{{0,0,1,1}},out));
    check("overlapping existing items rejected",!outfit::place(4,4,{{0,0,2,2},{1,1,2,2}},{{0,0,1,1}},out));
    check("partial plan discarded",!outfit::place(2,2,{},{{0,0,1,1},{0,0,3,3}},out)&&out.empty());
    check("adjacent boundaries",!outfit::overlaps({0,0,2,2},{2,0,2,2}));
    check("overflow dimensions rejected",!outfit::place(2147483647,4,{},{{0,0,1,1}},out));
    outfit::Request r{};r.magic=0x4c4c4f46;r.version=1;r.count=1;r.holder=0x10000;r.save=0x20000;
    check("request ABI valid",outfit::requestValid(r));r.count=17;check("too many items rejected",!outfit::requestValid(r));
    r.count=1;r.preset=4;check("out of range preset rejected",!outfit::requestValid(r));
    std::printf("Outfit core: %d checks, %d failed\n",tests,fails);return fails?1:0;
}
