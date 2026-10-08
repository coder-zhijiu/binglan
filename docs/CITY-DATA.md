# 内置离线城市库

> 记录城市库的数据来源、许可证、生成方式与已知边界。按 `AGENTS.md` 第 8 节要求，任何随程序分发的第三方数据都应在此登记。

## 为什么有这个文件

天气城市此前只能通过 OpenStreetMap Nominatim 在线搜索。`nominatim.openstreetmap.org` 在中国大陆网络下直连不可达（DNS 污染 + 连接超时），用户因此无法设置城市、天气无法使用（2026-10-08 本机实测，见当次任务记录）。内置离线城市库后，国内省、市、区县搜索完全在本地完成，境外冷门地名仍走 Nominatim 在线兜底。

数据文件：`src/BingLan.Core/Assets/city-library.cn.json`（作为 EmbeddedResource 编入 `BingLan.Core`，运行时由 `CityLibrary.LoadDefault()` 加载）。

## 数据来源

| 内容 | 来源 | 许可证 |
| --- | --- | --- |
| 省、市、区县名称与层级（GB/T 2260 区划代码推导） | [mumuy/data_location](https://github.com/mumuy/data_location) `list.json` | MIT |
| 地名经纬度坐标 | [pyecharts](https://github.com/pyecharts/pyecharts) `pyecharts/datasets/city_coordinates.json` | MIT |

两个仓库均为 MIT 许可，允许随程序再分发。生成日期：2026-10-08。

## 生成脚本

```python
# 依赖：hierarchy.json = mumuy/data_location list.json
#       city_coordinates.json = pyecharts 数据文件
import json, math

coords = json.load(open('city_coordinates.json', encoding='utf-8'))
flat = json.load(open('hierarchy.json', encoding='utf-8'))

SHORT_FORMS = {
    '新疆维吾尔自治区': '新疆', '内蒙古自治区': '内蒙古', '广西壮族自治区': '广西',
    '宁夏回族自治区': '宁夏', '西藏自治区': '西藏', '香港特别行政区': '香港',
    '澳门特别行政区': '澳门',
}
SUFFIX_WORDS = ['特别行政区', '自治区', '自治州', '自治县', '自治旗', '地区', '林区',
                '矿区', '新区', '市', '区', '县', '盟', '旗', '州', '省']

def name_candidates(name):
    seen, queue = set(), [name]
    while queue:
        cur = queue.pop()
        if cur in seen or len(cur) < 2:
            continue
        seen.add(cur)
        if cur in SHORT_FORMS:
            queue.append(SHORT_FORMS[cur])
        for w in SUFFIX_WORDS:
            if cur.endswith(w) and len(cur) > len(w):
                queue.append(cur[:-len(w)])
    return seen

def find_coord(name):
    for cand in name_candidates(name):
        if cand in coords:
            return coords[cand]
    return None

def dist_km(a, b):
    lon1, lat1, lon2, lat2 = map(math.radians, [a[0], a[1], b[0], b[1]])
    return 6371 * math.acos(min(1, math.sin(lat1)*math.sin(lat2) +
                                math.cos(lat1)*math.cos(lat2)*math.cos(lon1-lon2)))

provinces = {c: n for c, n in flat.items() if c.isdigit() and len(c) == 6 and c[2:4] == '00' and c[4:6] == '00'}
cities = {c: n for c, n in flat.items() if c.isdigit() and len(c) == 6 and c[2:4] != '00' and c[4:6] == '00'}
districts = {c: n for c, n in flat.items() if c.isdigit() and len(c) == 6 and c[2:4] != '00' and c[4:6] != '00'}
prov_coord = {c: find_coord(n) for c, n in provinces.items()}

entries = []
def add(name, path, coord):
    if coord:
        entries.append({"n": name, "p": path, "la": round(coord[1], 4), "lo": round(coord[0], 4)})

MUNICIPAL = {'11', '12', '31', '50'}
for pp in list(MUNICIPAL) + ['81', '82']:      # 直辖市与港澳作为可搜索条目
    name = provinces.get(pp + '0000')
    if not name:
        continue
    if pp in MUNICIPAL:
        add(name, name, prov_coord.get(pp + '0000'))
    else:
        add(SHORT_FORMS.get(name, name), '', prov_coord.get(pp + '0000'))

for code, name in sorted(cities.items()):
    pp = code[:2]
    if pp in MUNICIPAL or pp in {'81', '82'}:
        continue
    add(name, provinces.get(pp + '0000', ''), find_coord(name) or prov_coord.get(pp + '0000'))

for code, name in sorted(districts.items()):
    pp, cc = code[:2], code[:4] + '00'
    if pp in MUNICIPAL or pp in {'81', '82'}:
        path = provinces.get(pp + '0000', '')
        ref = prov_coord.get(pp + '0000')
    else:
        path = (provinces.get(pp + '0000', '') + ' ' + cities.get(cc, '')).strip()
        ref = find_coord(cities.get(cc, '')) or prov_coord.get(pp + '0000')
    raw = find_coord(name)
    # 同名区县消歧：坐标离上级城市超过 150 公里时不可信，回退到上级坐标。
    if raw is not None and (ref is None or dist_km(raw, ref) <= 150):
        final = raw
    elif ref is not None:
        final = ref
    else:
        continue
    add(name, path, final)

json.dump({"schema": 1, "entries": entries},
          open('city-library.cn.json', 'w', encoding='utf-8'),
          ensure_ascii=False, separators=(',', ':'))
```

## 规模与质量（2026-10-08 生成）

- 条目：3,405（4 直辖市 + 2 港澳 + 338 地级 + 其余区县级）；文件 236,677 字节。
- 坐标来源：2,310 条直接命中，747 条回退到上级市/省坐标，0 条丢弃。
- 同名区县（西湖区 杭州/南昌、朝阳区 北京/长春等）全部成对保留且坐标各归其位。

## 已知边界

- 坐标精度：pyecharts 坐标多数保留两位小数（约 1 km），且可能来自百度坐标系，与 WGS84 存在数百米到约 2 km 偏移。天气是区域数据，该误差无实际影响。
- 上级坐标回退的 747 条（多在西藏、青海、新疆等），精度为市/省级中心，天气场景同样足够。
- 乡镇/街道级地名不在库内；这部分和所有境外地名继续走 Nominatim 在线搜索（受其可达性限制）。
- 不支持拼音或拼音首字母搜索，只支持中文名及其组合（“杭州西湖”“朝阳 北京”）。
- 港澳台：香港、澳门为单条目；台湾按 mumuy 数据中的省市层级收录。

## 更新方式

区划和坐标都会缓慢变化。更新时重新下载两个上游文件、运行上述脚本、替换 `city-library.cn.json`，并在 `tests/BingLan.InformationTests/CityLibraryTests.cs` 的条目数下限与抽查点失效时同步调整。
