# 创意工坊发布文案

四个 Mod 各自上传成一个创意工坊物品。游戏用 `config.json` 的 `name`、`author`、`version`、`description`、`url` 填写工坊资料；`description` 也会出现在游戏内模组列表。长介绍和截图在上传成功后，到创意工坊网页上补。

`url` 指向 <https://github.com/yangyang8599/InfraSpace_mod>。玩家有问题到 [Issues](https://github.com/yangyang8599/InfraSpace_mod/issues) 反馈，并写明是哪一个模组。

官方步骤：<https://forum.dionicsoftware.com/t/infraspace-modding-tutorial-publishing-your-mod-to-the-steam-workshop/3143>

## 上传

1. 设置里打开开发者模式，模组页才会出现 Upload。
2. 每个模组目录只留这四个文件：`模组名.dll`、`0Harmony.dll`、`config.json`、`logo.jpg`。
3. 在游戏模组页对这一个模组点 Upload。成功后打开给出的链接。
4. 第一次发布要在 Steam 上接受创意工坊协议，否则别人看不到。
5. 在网页上换成下面的长介绍，并按清单上传截图。
6. 换电脑时用同一 Steam 账号订阅。游戏会把物品下到 `steamapps\workshop\content\1511460\`，不必再复制文件夹。

上传成功后，游戏会在该模组目录写入 `publishedFileId.txt`。它把本地目录和这一件工坊物品绑在一起。之后改文件，回到模组页点 Update。不要删这个文件，也不要把它复制到另一个模组，否则会改错物品，或再新建一件重复物品。

当前安装目录里还有 `.dll.old`、`.dll.previous` 和 `InfraSpaceSimProbe`。这些不要带进工坊包。`InfraSpaceSimProbe` 是本地探针，不发布。

`logo.jpg` 放在模组目录根上，256×256，必须小于 1MB。它同时是游戏内模组列表图标和工坊预览图。四张图标已经放进各自的源码目录和安装目录：建造是琥珀色框选，物流是青色双枢纽，生产是绿色柱状图，车站是铜色分岔。左下角都有同一枚浅色圆底 Y，用来标出这是同一系列。

`config.json` 的 `name` 不要改。程序集文件名必须仍以这个名字开头，游戏靠它加载。

## 玩家需要知道的存档情况

四个 Mod 可以单独订阅。车站工具在检测到建造工具的复制时，会一并复制车站端口；没装建造工具也不影响车站工具自己加载。

用过这些模组的存档会记下对应入口。以后打开这个存档时，这些模组需要仍然启用。

- 建造工具、生产分析：不新增建筑或道路类型。卸掉后世界对象仍是原版对象，但这个存档仍然期望模组还在。
- 车站工具：车站本身还在。卸掉后，端口方向和后续编辑不一定还按原来的选择工作。
- 多物资物流：每种物资的选择、容量、半径和“仅存储”写在存档里。卸掉后这些调度会丢失或退回原版。

## 截图还缺什么

游戏没在运行，下面这些画面要在游戏里截，再传到工坊网页。每张图只演示一件事，界面用游戏语言，不要带开发者控制台。

| 模组 | 要拍的画面 |
| --- | --- |
| 建造工具 | 框选后的移动或复制预览，能看出可以旋转；按 `H` 后新路不再吸到旧路；按 `J` 后新路沿原方向拉长；按住建筑约半秒后的蓝色产率连线；按住 `Shift` 升级整段铁路时的总价 |
| 多物资物流 | 中心面板上多个物资图标，其中有一个变红；容量滑块和库存数字；地图上同一中心的绿、红、黄建筑；工厂面板上写明由哪座中心配送或搜集 |
| 生产分析 | 生产概况里带下划线的消费和建筑数量；选中地区后的规划模式，最终产物为蓝或绿；电力可视化里两张不同颜色的电网；点资源图标后，生产建筑变绿 |
| 车站工具 | 放置或编辑车站时，轨道端口和公路出入口方向不同；线路面板里拖动站点排序；鼠标停在车站或列车物资上，能看到发出建筑、收货建筑、下一站和数量 |

## 建造工具

已发布：<https://steamcommunity.com/sharedfiles/filedetails/?id=3829349603>

工坊标题：`Construction Tools`

`config.json` 短说明已经是：

> Move or copy a group of buildings and roads, rotate the preview, toggle road snapping, extend roads in a straight line, see which neighbors boost each other's output, and upgrade a connected railway at once.

网页长介绍：

```text
[h1]建造工具[/h1]
框选一组建筑和道路，整体移动或复制。预览时可以用镜头旋转键转 90 度。复制时按住 Shift 微调位置。复制仍走原版建造和资源结算。

[list]
[*]Ctrl+M：批量移动。道路在选框边缘切开，建筑整组选中。
[*]Ctrl+C：复制选中的建筑和道路。
[*]H：铺路时开关磁吸。关掉后不再吸到已有道路，网格对齐还在。
[*]J：铺公路或铁路时沿已有方向笔直延长，鼠标只改变长度。空地起笔仍跟着鼠标。
[*]移动或放置时，原版黄线指向能提高它产率的邻居；蓝线指向靠它提高产率的建筑。平时按住建筑约半秒才显示。
[*]铁路替换时按住 Shift，升级整条连通且可编辑的铁路，并显示总价。
[/list]

不新增建筑或道路类型。用这个模组保存过的存档，之后打开时需要继续启用它。

问题和建议请到 https://github.com/yangyang8599/InfraSpace_mod/issues 提交 Issue，并写明是建造工具。

[h1]Construction Tools[/h1]
Move or copy a boxed group of buildings and roads. Rotate the preview with the game's camera-rotation keys. Hold Shift while copying to nudge the position. Placement and costs still use the base game.

[list]
[*]Ctrl+M moves the selection. Roads are cut at the box edge; buildings move as a group.
[*]Ctrl+C copies the selected buildings and roads.
[*]H toggles snapping onto existing roads. Grid alignment stays on.
[*]J extends a new road or rail straight out from an existing node. The pointer only changes the length.
[*]Yellow links show neighbors that boost this building. Blue links show buildings it boosts. Hold a finished building for about half a second to see them.
[*]Hold Shift with the rail upgrade tool to upgrade the connected editable track and see the total cost.
[/list]

This mod does not add new building or road types. A save that was played with it enabled still expects the mod when you load it again.

Report bugs at https://github.com/yangyang8599/InfraSpace_mod/issues and name Construction Tools.
```

## 多物资物流

已发布：<https://steamcommunity.com/sharedfiles/filedetails/?id=3829355527>

工坊标题：`Multi-Resource Logistics`

短说明：

> Let collection and distribution centers handle several resources at once, each with its own stock, capacity, and radius, and warn when a truck cannot reach its partner.

网页长介绍：

```text
[h1]多物资物流[/h1]
搜集中心和配送中心可以同时处理多种物资。每种物资有自己的库存、容量和覆盖半径，单项容量最高 2000。

[list]
[*]用末尾的 + 添加物资。右键移除；还有库存或在途车辆时会先确认。
[*]左键图标变成灰色：只保留库存，不再搜集、配送或转给配对中心。
[*]图标变红表示没有配对中心，或者货车开不过去。鼠标放上去能看到是哪两座中心。
[*]左键库存数字，切换地图和半径滑块正在看的那一种物资。范围内能送到是绿色，范围内没路是红色，半径外仍在生产或消耗是黄色。
[*]仅存储的物资不发车，也不给建筑上色。
[*]中心之间转运双方都选了的物资。范围内货车到不了的建筑继续走公共供需。
[*]工厂的输入和输出会写明在途数量，以及由哪座中心处理，或交给公共供需。
[*]建筑头顶的警示只在最近一分钟运入明显低于运出，并且库存低于目标一半时出现。
[/list]

这些选择保存在存档里。卸掉模组后，多种物资的容量、半径和调度会丢失或退回原版。

问题和建议请到 https://github.com/yangyang8599/InfraSpace_mod/issues 提交 Issue，并写明是多物资物流。

[h1]Multi-Resource Logistics[/h1]
Collection centers and distribution centers can handle several resources at once. Each resource has its own stock, capacity, and radius. One resource can store up to 2000.

[list]
[*]Add a resource with +. Right-click removes it, and asks first if stock or trucks are still using it.
[*]Left-click an icon to make that resource storage-only.
[*]A red icon means there is no partner center, or trucks cannot travel between the pair. The tooltip names the centers.
[*]Left-click the stock number to choose which resource the map and radius use. Green means reachable, red means inside the radius but no road, yellow means outside the radius but still produced or consumed.
[*]Storage-only resources are not dispatched and do not color buildings.
[*]Linked centers transfer resources selected by both. Buildings the trucks cannot reach stay on public supply.
[*]Factory input and output tips show goods on the way, and which center handles them.
[*]A building warning appears when the last minute brought in less than 90% of what left, and stock is under half the target.
[/list]

These choices are saved with the game. Removing the mod drops the extra resources, capacities, and dispatch rules.

Report bugs at https://github.com/yangyang8599/InfraSpace_mod/issues and name Multi-Resource Logistics.
```

## 生产分析

已发布：<https://steamcommunity.com/sharedfiles/filedetails/?id=3829352111>

工坊标题：`Production Insights`

短说明：

> Clearer production overview: hide empty district rows, compare recent output, plan from buildings already placed, color each power grid, and highlight buildings that make or need a resource.

网页长介绍：

```text
[h1]生产分析[/h1]
生产概况更好读，不改工厂配方、生产速度或库存。

[list]
[*]选中地区时，就业、生产和消费都是 0 的行会隐藏。全局视图不隐藏。
[*]资源图标底部是正在生产它的建筑数。未选地区按全图，选中地区只算该地区。
[*]产量和消耗的列表头可以在原版数字、这一拍、最近连续 1 分钟之间切换。这个开关不写入存档。规划模式打开时不切换。
[*]选中地区后可以打开规划模式。产出按已建建筑和配方计算，不看开工。消耗从最终产物倒推。
[*]“无产原矿”让倒推只走已经建好的生产建筑。“高科工具”让已安装的高科技工具按 1.5 倍计入。
[*]最终产物的产量是蓝色。富余超过该区单座最高产量时是绿色。
[*]点开工厂后，“生产：”后面按当前效率显示每分钟消耗和产出。
[*]电力可视化里，互不相连的电网使用不同颜色。最大的一组仍是原版淡黄色。
[*]单击资源图标，生产它的建筑变绿；双击则把需要它的工厂和住宅标成蓝色。可视化面板里也可以按产物查看。
[/list]

用这个模组保存过的存档，之后打开时需要继续启用它。它不改模拟结果。

问题和建议请到 https://github.com/yangyang8599/InfraSpace_mod/issues 提交 Issue，并写明是生产分析。

[h1]Production Insights[/h1]
The production overview is easier to read. Recipes, production speed, and stock stay unchanged.

[list]
[*]In a district, rows with no workers, production, or consumption are hidden. The global view is not filtered.
[*]The number under a resource icon is how many buildings produce it, for the whole map or the selected district.
[*]The production and consumption headers switch between the base numbers, the current tick, and the last continuous minute. This choice is not saved. It is unavailable while planning mode is on.
[*]Planning mode estimates output from placed buildings and recipes, and works consumption back from final products.
[*]“No raw extraction” stops the estimate at buildings you have already placed. “High-tech tools” counts installed tools at 1.5×.
[*]Final-product output is blue. A surplus larger than the district's best single building is green.
[*]An open factory shows its current per-minute inputs and outputs next to Production.
[*]Disconnected power grids use different colors. The largest grid stays the original pale yellow.
[*]Click a resource icon to mark its producers green. Double-click to mark factories and homes that need it blue.
[/list]

A save played with this mod still expects it on the next load. The simulation itself is unchanged.

Report bugs at https://github.com/yangyang8599/InfraSpace_mod/issues and name Production Insights.
```

## 车站工具

已发布：<https://steamcommunity.com/sharedfiles/filedetails/?id=3829354211>

工坊标题：`Station Tools`

短说明：

> Set each station's rail and road port direction, start trains toward the next stop, reorder a line by dragging, and see where cargo came from and where it is going.

网页长介绍：

```text
[h1]车站工具[/h1]
调整车站端口和列车方向。面板上的具体按键会随当前是放置还是编辑而变化。

[list]
[*]放置车站时，分别切换每个轨道端口的方向，以及公路端口是入口还是出口。
[*]已建车站可以再进入端口编辑。移动、拆除和重建时保留这些选择。
[*]新增列车时，可以选择沿轨道更快到达下一站的那一侧，避免环线反向绕行。
[*]线路面板里可以拖动左侧线路或右侧停靠站来改顺序。
[*]鼠标停在车站或列车的物资上，可以看到发出建筑、最终收货建筑、下一站和数量。
[*]如果同时启用了建造工具，蓝图复制会带上车站端口。没装建造工具时，本模组仍可单独使用。
[/list]

车站和轨道仍是原版对象。端口方向保存在模组里，卸掉后这些选择不一定还能按原样编辑。

问题和建议请到 https://github.com/yangyang8599/InfraSpace_mod/issues 提交 Issue，并写明是车站工具。

[h1]Station Tools[/h1]
Set station ports and train direction. The panel shows the keys for the current placement or edit mode.

[list]
[*]While placing a station, set each rail port's direction and each road port as an entrance or exit.
[*]Existing stations can be edited again. Moving, removing, and rebuilding keeps the port choices.
[*]A new train can start on the track that reaches the next stop sooner, instead of going the long way around a loop.
[*]Drag a line or a stop in the line panel to reorder it.
[*]Hover cargo at a station or on a train to see the sender, final receiver, next stop, and amount.
[*]With Construction Tools installed, blueprint copy includes station ports. This mod also works on its own.
[/list]

Stations and tracks remain base-game objects. Port choices belong to the mod, and removing it can make later edits lose those choices.

Report bugs at https://github.com/yangyang8599/InfraSpace_mod/issues and name Station Tools.
```
