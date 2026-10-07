// Loaded on an explicit outfit command only. Dispatches through UnitySynchronizationContext;
// does not patch executable code, call Unity from a worker, or write save files.
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <string>
#include <cstring>
#include <memory>
#include "OutfitCore.h"

namespace {
using Obj=void*; using Klass=void*; using Method=const void*; using Field=void*;
struct Api {
    HMODULE mod=nullptr;
    void* (*domain)();
    const void** (*assemblies)(void*,size_t*);
    const void* (*image)(const void*);
    const char* (*imageName)(const void*);
    const void* (*corlib)();
    Klass (*klass)(const void*,const char*,const char*);
    Klass (*objClass)(Obj);
    Klass (*parent)(Klass);
    const char* (*name)(Klass);
    Field (*field)(Klass,const char*);
    void (*get)(Obj,Field,void*);
    void (*set)(Obj,Field,void*);
    void (*staticGet)(Field,void*);
    Method (*method)(Klass,const char*,int);
    Obj (*invoke)(Method,Obj,void**,Obj*);
    Obj (*unbox)(Obj);
    Obj (*box)(Klass,void*);
    Obj (*newObj)(Klass);
    uintptr_t (*root)(Obj,bool);
    void (*unroot)(uintptr_t);
    bool (*inited)(Klass);
    Obj (*attach)(void*);
    void (*detach)(Obj);
    Obj* (*threads)(size_t*);
    template<class T> void bind(T& f,const char* n) {
        f=reinterpret_cast<T>(GetProcAddress(mod,n));
        if (!f) throw std::runtime_error(std::string("missing export: ")+n);
    }
    void init() {
        mod=GetModuleHandleW(L"GameAssembly.dll");
        if (!mod) throw std::runtime_error("GameAssembly absent");
#define B(member,exportName) bind(member,"il2cpp_" exportName)
        B(domain,"domain_get");B(assemblies,"domain_get_assemblies");B(image,"assembly_get_image");
        B(imageName,"image_get_name");B(corlib,"get_corlib");B(klass,"class_from_name");
        B(objClass,"object_get_class");B(parent,"class_get_parent");B(name,"class_get_name");
        B(field,"class_get_field_from_name");B(get,"field_get_value");B(set,"field_set_value");
        B(staticGet,"field_static_get_value");
        B(method,"class_get_method_from_name");B(invoke,"runtime_invoke");B(unbox,"object_unbox");
        B(box,"value_box");B(newObj,"object_new");B(root,"gchandle_new");B(unroot,"gchandle_free");
        B(inited,"class_is_inited");
        B(attach,"thread_attach");B(detach,"thread_detach");B(threads,"thread_get_all_attached_threads");
#undef B
    }
    Field f(Obj o,const char* n) {
        for (Klass c=objClass(o);c;c=parent(c)) if (Field x=field(c,n)) return x;
        throw std::runtime_error(std::string("missing field: ")+n);
    }
    template<class T> T val(Obj o,const char* n) { if (!o) throw std::runtime_error("null object");T v{};get(o,f(o,n),&v);return v; }
    template<class T> void put(Obj o,const char* n,T v) { set(o,f(o,n),&v); }
    Method meth(Klass c,const char* n,int cnt) {
        for (;c;c=parent(c)) if (Method m=method(c,n,cnt)) return m;
        throw std::runtime_error(std::string("missing method: ")+n);
    }
    Obj call(Method m,Obj o,void** args=nullptr) {
        Obj exc=nullptr;Obj ret=invoke(m,o,args,&exc);
        if (exc) throw std::runtime_error(std::string("game exception: ")+name(objClass(exc)));
        return ret;
    }
    Obj call(Obj o,const char* n,int cnt,void** args=nullptr) { return call(meth(objClass(o),n,cnt),o,args); }
    Klass gameClass(const char* n) {
        size_t cnt=0;const void** list=assemblies(domain(),&cnt);
        for (size_t i=0;i<cnt;++i) if (std::strcmp(imageName(image(list[i])),"Assembly-CSharp.dll")==0)
            if (Klass c=klass(image(list[i]),"",n)) return c;
        throw std::runtime_error(std::string("missing game class: ")+n);
    }
    bool is(Obj o,const char* n) { return o && std::strcmp(name(objClass(o)),n)==0; }
    bool boolean(Obj o) { if (!o) throw std::runtime_error("missing bool result");return *static_cast<bool*>(unbox(o)); }
};

// Array header is IL2CPP x64: bounds @0x10, length @0x18, data @0x20.
std::vector<Obj> collection(Api& a,Obj o) {
    if (!o) throw std::runtime_error("missing collection");
    Obj arr=o;int count=0;
    const char* n=a.name(a.objClass(o));
    if (std::strchr(n,'[')) count=static_cast<int>(*reinterpret_cast<uintptr_t*>(static_cast<char*>(o)+24));
    else { arr=a.val<Obj>(o,"_items");count=a.val<int>(o,"_size"); }
    if (count<0 || count>2000) throw std::runtime_error("invalid collection length");
    if (count==0) return {};
    if (!arr || *reinterpret_cast<uintptr_t*>(static_cast<char*>(arr)+24)<static_cast<uintptr_t>(count))
        throw std::runtime_error("invalid collection array");
    Obj* data=reinterpret_cast<Obj*>(static_cast<char*>(arr)+32);
    return std::vector<Obj>(data,data+count);
}

struct Backup {
    Obj item, presets, colors;
    int x,y,state,inventory;
};
struct Job {
    Api a; outfit::Request request{};
    HANDLE done=CreateEventW(nullptr,TRUE,FALSE,nullptr);
    volatile LONG phase=0, refs=2;
    Obj context=nullptr;
    std::vector<uintptr_t> roots;
    std::vector<Backup> backups;
    void keep(Obj o) { if (o) roots.push_back(a.root(o,false)); }
    ~Job(){ for (uintptr_t h:roots) a.unroot(h);if(done)CloseHandle(done); }
};
void release(Job* j) { if (InterlockedDecrement(&j->refs)==0) delete j; }
void msg(Job* j,const char* s) { strncpy_s(j->request.message,s,_TRUNCATE); }
void identity(Job* j,Obj dm=nullptr) {
    auto& a=j->a;Obj holder=reinterpret_cast<Obj>(j->request.holder),save=reinterpret_cast<Obj>(j->request.save);
    if (!a.is(holder,"DataHolder") || !a.is(save,"SaveData") || a.val<Obj>(holder,"CurrentData")!=save
        || a.val<int>(save,"SelectedPresetIndex")!=j->request.preset)
        throw std::runtime_error("save or preset changed; cancelled");
    if (dm && (a.val<Obj>(dm,"holder")!=holder || !a.val<Obj>(dm,"m_CachedPtr")))
        throw std::runtime_error("outfit window was closed; cancelled");
}
std::wstring equip(Api& a,Obj it) {
    Obj raw=a.val<Obj>(it,"RawEquipType");
    int length=raw?*reinterpret_cast<int*>(static_cast<char*>(raw)+16):0;
    return length>0 && length<256?std::wstring(reinterpret_cast<wchar_t*>(static_cast<char*>(raw)+20),length):L"";
}
void backup(Job* j,Obj it) {
    auto& a=j->a;
    Obj presets=a.val<Obj>(it,"presetIndices"),colors=a.val<Obj>(it,"colorValues");
    if (presets) presets=a.call(presets,"Clone",0);
    if (colors) colors=a.call(colors,"Clone",0);
    Backup b{it,presets,colors,
        a.val<int>(it,"X"),a.val<int>(it,"Y"),a.val<int>(it,"CurrentState"),a.val<int>(it,"InventoryType")};
    j->keep(it);j->keep(b.presets);j->keep(b.colors);j->backups.push_back(b);
}
void restore(Job* j) {
    identity(j);
    for (const auto& b:j->backups) {
        j->a.put(b.item,"presetIndices",b.presets);j->a.put(b.item,"colorValues",b.colors);
        j->a.put(b.item,"X",b.x);j->a.put(b.item,"Y",b.y);
        j->a.put(b.item,"CurrentState",b.state);j->a.put(b.item,"InventoryType",b.inventory);
    }
}
void refresh(Job* j,Obj dm) {
    int body=j->a.val<int>(reinterpret_cast<Obj>(j->request.save),"CurrentBodySize");
    void* args[]={&body};j->a.call(dm,"ResetDressesAsync",1,args);
}

void execute(Job* j) {
    auto& a=j->a;identity(j);
    Obj holder=reinterpret_cast<Obj>(j->request.holder),save=reinterpret_cast<Obj>(j->request.save);
    Obj dm=a.val<Obj>(holder,"dressManager");
    if (!dm) dm=a.val<Obj>(holder,"cachedDressManager");
    if (!a.is(dm,"DressManager") || !a.val<Obj>(dm,"m_CachedPtr") || a.val<int>(dm,"inventoryMode")!=4)
        throw std::runtime_error("open the base outfit window first");
    identity(j,dm);
    Obj dc=a.val<Obj>(dm,"_MasterDressController");
    if (!a.is(dc,"DressController") || !a.val<Obj>(dc,"m_CachedPtr")
        || a.val<bool>(dc,"_Updating") || a.val<bool>(dc,"IsDoingSex")
        || a.val<bool>(dm,"isOutfitChangeInProgress") || a.val<bool>(dm,"isFadeInProgress"))
        throw std::runtime_error("avatar is busy; try again when loading finishes");
    Obj inv=a.val<Obj>(save,"InventoryData");
    if (!a.is(inv,"InventoryData")) throw std::runtime_error("invalid inventory");
    Obj itemList=a.val<Obj>(inv,"items");
    auto all=collection(a,itemList);
    std::vector<Obj> chosen;
    int occupiedSlots=0;
    for (uint32_t i=0;i<j->request.count;++i) {
        Obj it=reinterpret_cast<Obj>(j->request.items[i]);
        if (std::find(all.begin(),all.end(),it)==all.end() || !a.is(it,"ItemData")
            || a.val<int>(it,"DatabaseID")!=j->request.ids[i] || a.val<int>(it,"ItemType")!=1)
            throw std::runtime_error("selected item identity changed");
        int type=a.val<int>(it,"InventoryType");
        if (type!=0 && type!=1 && type!=2 && type!=3) throw std::runtime_error("selected item is not owned");
        int slots=outfit::mask(equip(a,it));
        if (!slots || (slots&4096) || (slots&occupiedSlots)) throw std::runtime_error("selected item equip slots changed");
        occupiedSlots|=slots;
        if (std::find(chosen.begin(),chosen.end(),it)!=chosen.end()) throw std::runtime_error("duplicate outfit item");
        chosen.push_back(it);
    }
    int preset=j->request.preset;void* pa[]={&preset};
    auto equipped=collection(a,a.call(inv,"FindItemsByPreset",1,pa));
    // Older migrated saves can still expose equipped items without a preset array.
    for (Obj it:all) if (a.val<int>(it,"CurrentState")==2 && a.val<int>(it,"InventoryType")==0
        && std::find(equipped.begin(),equipped.end(),it)==equipped.end()) equipped.push_back(it);
    std::vector<Obj> removed,stored;
    for (Obj it:equipped) {
        if (std::find(chosen.begin(),chosen.end(),it)!=chosen.end()) continue;
        // Preserve hair even when it is part of the current preset.
        std::wstring s=equip(a,it);
        if (s.find(L"Hair")!=std::wstring::npos || a.val<int>(it,"ItemType")!=1) continue;
        int slots=outfit::mask(s);
        if (!slots) throw std::runtime_error("equipped garment metadata unavailable");
        if (!outfit::conflicts(slots,occupiedSlots)) continue;
        removed.push_back(it);
        if (!a.boolean(a.call(it,"IsInOtherPresets",1,pa))) stored.push_back(it);
    }
    int storage=3;void* sa[]={&storage};Obj size=a.call(inv,"GetInventorySize",1,sa);
    if (!size) throw std::runtime_error("missing storage dimensions");
    const float* dims=static_cast<const float*>(a.unbox(size));
    int storageWidth=0,storageHeight=0;
    if (!dims || !outfit::gridSize(dims[0],dims[1],storageWidth,storageHeight))
        throw std::runtime_error("invalid storage dimensions; nothing changed");
    std::vector<outfit::Rect> occupied,toStore,positions;
    for (Obj it:all) if (a.val<int>(it,"InventoryType")==3
        && std::find(chosen.begin(),chosen.end(),it)==chosen.end())
        occupied.push_back({a.val<int>(it,"X"),a.val<int>(it,"Y"),a.val<int>(it,"Width"),a.val<int>(it,"Height")});
    for (Obj it:stored) toStore.push_back({0,0,a.val<int>(it,"Width"),a.val<int>(it,"Height")});
    if (!outfit::place(storageWidth,storageHeight,occupied,toStore,positions))
        throw std::runtime_error("not enough storage space; nothing changed");
    for (Obj it:removed) backup(j,it);
    for (Obj it:chosen) backup(j,it);
    Method state=a.meth(a.objClass(inv),"SetItemState",4);
    identity(j,dm);
    try {
        for (Obj it:removed) {
            Obj guid=a.val<Obj>(it,"guid");void* rp[]={guid,&preset};
            auto pos=std::find(stored.begin(),stored.end(),it);
            if (pos==stored.end()) {
                a.call(inv,"RemoveItemFromPreset",2,rp);
                a.put(it,"CurrentState",0);a.put(it,"InventoryType",0);
                a.put(it,"X",0);a.put(it,"Y",0);
            } else {
                int off=0;void* sp[]={guid,&off,&storage,&preset};a.call(state,inv,sp);
                const auto& r=positions[static_cast<size_t>(pos-stored.begin())];
                a.put(it,"X",r.x);a.put(it,"Y",r.y);
            }
        }
        for (Obj it:chosen) {
            int on=2,type=0;Obj guid=a.val<Obj>(it,"guid");void* sp[]={guid,&on,&type,&preset};
            a.call(state,inv,sp);a.put(it,"X",0);a.put(it,"Y",0);
            if (!a.boolean(a.call(it,"BelongsToPreset",1,pa)) || a.val<int>(it,"CurrentState")!=2)
                throw std::runtime_error("game did not accept the outfit item");
        }
        identity(j,dm);refresh(j,dm);
        j->request.applied=static_cast<int>(chosen.size());j->request.result=0;
        msg(j,"outfit applied; native avatar refresh requested");
    } catch (...) {
        restore(j);refresh(j,dm);throw;
    }
}

void mainSafe(Job* j) {
    try { execute(j); }
    catch (const std::exception& e) { j->request.result=2;msg(j,e.what()); }
    catch (...) { j->request.result=2;msg(j,"unexpected outfit failure"); }
}
void recoverSafe(Job* j) {
    try { if (!j->backups.empty()) restore(j); }
    catch (...) { }
}
void __fastcall callback(void*,Obj state,Method) {
    Job* j=*static_cast<Job**>(static_cast<void*>(static_cast<char*>(state)+16));
    if (InterlockedCompareExchange(&j->phase,1,0)==0) {
        __try { mainSafe(j); }
        __except(EXCEPTION_EXECUTE_HANDLER) {
            __try { recoverSafe(j); } __except(EXCEPTION_EXECUTE_HANDLER) { }
            j->request.result=3;msg(j,"native exception; verify the outfit in game");
        }
    } else { j->request.result=4;msg(j,"outfit command cancelled before execution"); }
    InterlockedExchange(&j->phase,2);SetEvent(j->done);release(j);
}

Obj findContext(Api& a) {
    // The game's async library keeps the main context rooted in an initialized static field.
    size_t images=0;const void** assemblies=a.assemblies(a.domain(),&images);
    for (size_t i=0;i<images;++i) {
        Klass c=a.klass(a.image(assemblies[i]),"Naninovel.Async","PlayerLoopHelper");
        if (!c || !a.inited(c)) continue;
        Field f=a.field(c,"unitySynchronizationContext");
        if (f) { Obj value=nullptr;a.staticGet(f,&value);if(a.is(value,"UnitySynchronizationContext"))return value; }
    }
    size_t cnt=0;Obj* list=a.threads(&cnt);
    for (size_t i=0;i<cnt;++i) {
        Obj ec=a.val<Obj>(list[i],"m_ExecutionContext");
        if (!ec) continue;
        Obj context=a.val<Obj>(ec,"_syncContext");
        if (a.is(context,"UnitySynchronizationContext")) return context;
        context=a.val<Obj>(ec,"_syncContextNoFlow");
        if (a.is(context,"UnitySynchronizationContext")) return context;
    }
    throw std::runtime_error("Unity main-thread context unavailable");
}
DWORD run(void* param) {
    auto* out=static_cast<outfit::Request*>(param);
    if (!out || !outfit::requestValid(*out)) return 1;
    Job* j=new Job();j->request=*out;Obj thread=nullptr;bool posted=false;
    try {
        j->a.init();thread=j->a.attach(j->a.domain());
        if (!thread) throw std::runtime_error("unable to attach IL2CPP worker");
        identity(j);j->keep(reinterpret_cast<Obj>(j->request.holder));j->keep(reinterpret_cast<Obj>(j->request.save));
        j->context=findContext(j->a);j->keep(j->context);
        Klass cbClass=j->a.klass(j->a.corlib(),"System.Threading","SendOrPostCallback");
        Klass intptr=j->a.klass(j->a.corlib(),"System","IntPtr");
        if (!cbClass || !intptr) throw std::runtime_error("missing callback classes");
        Obj cb=j->a.newObj(cbClass);j->keep(cb);
        void* fn=reinterpret_cast<void*>(&callback);void* zero=nullptr;
        j->a.put(cb,"method_ptr",fn);j->a.put(cb,"invoke_impl",fn);j->a.put(cb,"method_code",zero);
        Method method=j->a.meth(cbClass,"Invoke",1);j->a.put(cb,"method",method);
        Obj state=j->a.box(intptr,&j);j->keep(state);
        void* args[]={cb,state};j->a.call(j->context,"Post",2,args);posted=true;
        DWORD wait=WaitForSingleObject(j->done,10000);
        if (wait!=WAIT_OBJECT_0) {
            if (InterlockedCompareExchange(&j->phase,3,0)==0) {
                out->result=4;strncpy_s(out->message,"game did not process the command; cancelled",_TRUNCATE);
            } else if (WaitForSingleObject(j->done,30000)==WAIT_OBJECT_0) *out=j->request;
            else { out->result=5;strncpy_s(out->message,"game operation still running; check in game before retrying",_TRUNCATE); }
        } else *out=j->request;
    } catch (const std::exception& e) {
        out->result=2;strncpy_s(out->message,e.what(),_TRUNCATE);
    }
    Api saved=j->a;
    if (!posted) release(j);
    release(j);
    if (thread) saved.detach(thread);
    return out->result;
}
}
extern "C" __declspec(dllexport) DWORD WINAPI LilliaOutfitRun(void* p) {
    __try { return run(p); }
    __except(EXCEPTION_EXECUTE_HANDLER) { return 99; }
}
BOOL WINAPI DllMain(HINSTANCE,DWORD,LPVOID){return TRUE;}
