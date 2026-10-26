using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace InfraSpaceConstructionTools
{
    internal sealed class AreaMoveController : MonoBehaviour,
        IPointerListener,
        ICameraDragBlocker,
        ICameraRotationBlocker,
        BuildingSelectionReason
    {
        private enum MoveState
        {
            Idle,
            Selecting,
            Preview
        }

        private enum BatchKind
        {
            None,
            Move,
            Copy
        }

        private readonly HashSet<Building> selected = new HashSet<Building>();
        private MoveState state;
        private Vector2 selectionStart;
        private Vector2 selectionEnd;
        private IBatchPlacementSession placementSession;
        private bool initialized;
        private BatchKind batchKind;
        private int pendingBatchActivationFrames;
        private static GUIStyle hintStyle;

        internal bool OwnsMoveInput => state != MoveState.Idle;

        internal void Initialize()
        {
            if (initialized)
            {
                return;
            }

            initialized = true;
            WorldScripts.Inst.clickHandler.AddPointerListener(this);
            if (!WorldScripts.Inst.cameraMovement.cameraDragBlockers.Contains(this))
            {
                WorldScripts.Inst.cameraMovement.cameraDragBlockers.Add(this);
            }

            if (!WorldScripts.Inst.cameraMovement.cameraRotationBlockers.Contains(this))
            {
                WorldScripts.Inst.cameraMovement.cameraRotationBlockers.Add(this);
            }
        }

        public void HandlePointerDown(Vector2 pointerPosition)
        {
            if (!CanUseAreaMove())
            {
                return;
            }

            if (state == MoveState.Preview)
            {
                return;
            }

            selectionStart = pointerPosition;
            selectionEnd = pointerPosition;
            state = MoveState.Selecting;
        }

        public void HandlePointerHold(Vector2 pointerPosition)
        {
            if (state == MoveState.Selecting)
            {
                selectionEnd = pointerPosition;
            }
        }

        public void HandlePointerUp(Vector2 pointerPosition)
        {
            if (state == MoveState.Selecting)
            {
                selectionEnd = pointerPosition;
                BeginPreview();
                return;
            }

            if (state == MoveState.Preview)
            {
                TryCommit();
            }
        }

        public bool IsCameraDragBlocked()
        {
            return state == MoveState.Selecting || state == MoveState.Preview;
        }

        public bool IsCameraRotationBlocked()
        {
            return state == MoveState.Preview && batchKind == BatchKind.Copy;
        }

        public bool IsSelected(Building building)
        {
            return selected.Contains(building);
        }

        private void Update()
        {
            if (!initialized || WorldScripts.Inst == null)
            {
                return;
            }

            bool controlPressed =
                Input.GetKey(KeyCode.LeftControl) ||
                Input.GetKey(KeyCode.RightControl);
            if (!IsTextInputFocused())
            {
                if (controlPressed && Input.GetKeyDown(KeyCode.M))
                {
                    SetBatch(batchKind == BatchKind.Move ? BatchKind.None : BatchKind.Move);
                }
                else if (controlPressed && Input.GetKeyDown(KeyCode.C))
                {
                    SetBatch(batchKind == BatchKind.Copy ? BatchKind.None : BatchKind.Copy);
                }
                else if (!controlPressed && Input.GetKeyDown(KeyCode.M) && batchKind != BatchKind.None)
                {
                    SetBatch(BatchKind.None);
                }
            }

            if (batchKind != BatchKind.None &&
                pendingBatchActivationFrames > 0)
            {
                if (!WorldScripts.Inst.dashboard.IsMoving())
                {
                    WorldScripts.Inst.dashboard.moveButton.TrySetIsPressed(true);
                }

                pendingBatchActivationFrames--;
            }

            if (batchKind != BatchKind.None && Input.GetKeyDown(KeyCode.Escape))
            {
                SetBatch(BatchKind.None);
                return;
            }

            if (!WorldScripts.Inst.dashboard.IsMoving())
            {
                Cancel();
                if (pendingBatchActivationFrames <= 0)
                {
                    batchKind = BatchKind.None;
                }
                return;
            }

            if (state == MoveState.Preview)
            {
                if (batchKind == BatchKind.Copy)
                {
                    placementSession?.UpdatePreview(Input.mousePosition);
                }

                if (Input.GetButtonDown("Fire2"))
                {
                    Cancel();
                }
            }
        }

        private void LateUpdate()
        {
            if (!initialized || state != MoveState.Preview)
            {
                return;
            }

            if (batchKind == BatchKind.Move)
            {
                placementSession?.UpdatePreview(Input.mousePosition);
            }
        }

        private void SetBatch(BatchKind kind)
        {
            if (batchKind == kind)
            {
                return;
            }

            bool turningOn = kind != BatchKind.None;
            batchKind = kind;
            pendingBatchActivationFrames = turningOn ? 3 : 0;
            if (!turningOn || state != MoveState.Idle)
            {
                Cancel();
            }
        }

        private bool CanUseAreaMove()
        {
            return initialized &&
                   state == MoveState.Idle &&
                   batchKind != BatchKind.None &&
                   DionicGame.HasFinishedLoading() &&
                   WorldScripts.Inst.dashboard.IsMoving() &&
                   !WorldScripts.Inst.canvasHandler.IsMouseOverUI() &&
                   !DionicGame.GetModalManager().IsGameModal();
        }

        private void BeginPreview()
        {
            Rect selectionRect = GetSelectionRect();
            List<Building> buildings = SelectBuildings(selectionRect);
            List<RoadPiece> roads = NetworkClipper.Clip(selectionRect);
            if ((buildings.Count == 0 && roads.Count == 0) ||
                !TryGetTerrainPosition(selectionEnd, out Vector3 anchor))
            {
                state = MoveState.Idle;
                return;
            }

            foreach (Building building in buildings)
            {
                selected.Add(building);
                building.AddSelectionReason(this);
            }

            if (batchKind == BatchKind.Copy)
            {
                placementSession = new BlueprintCopySession(buildings, roads, anchor);
            }
            else
            {
                placementSession = new BuildingMoveSession(buildings, roads, anchor);
            }

            state = MoveState.Preview;
        }

        private List<Building> SelectBuildings(Rect screenRect)
        {
            HashSet<Building> result = new HashSet<Building>();
            Camera camera = Camera.main;
            foreach (Building building in WorldScripts.Inst.registry.builtBuildingsList.ToArray())
            {
                if (building == null ||
                    !building.IsValidAndBuilt() ||
                    !building.GetDescription().interactable ||
                    !WorldScripts.Inst.buildingConstructionManager.CanEditBuilding(building) ||
                    !BoundsIntersectsScreenRect(building.GetBounds(), screenRect, camera))
                {
                    continue;
                }

                AddBuildingAndGroup(result, building);
            }

            return result.ToList();
        }

        private static void AddBuildingAndGroup(HashSet<Building> result, Building building)
        {
            result.Add(building);
            BuildingGroup group = building.group;
            if (group == null)
            {
                return;
            }

            for (int i = 0; i < group.GetCount(); i++)
            {
                Building member = group.GetBuildingAt(i);
                if (member != null && member.IsValidAndBuilt())
                {
                    result.Add(member);
                }
            }
        }

        private void TryCommit()
        {
            if (placementSession == null)
            {
                Cancel();
                return;
            }

            if (placementSession.TryCommit() == BatchCommitResult.Success)
            {
                HandleCommitted();
            }
        }

        private void HandleCommitted()
        {
            ClearSelection();
            state = MoveState.Idle;
            placementSession = null;
        }

        private void Cancel()
        {
            placementSession?.Cancel();
            placementSession = null;
            ClearSelection();
            state = MoveState.Idle;
        }

        private void ClearSelection()
        {
            foreach (Building building in selected.ToArray())
            {
                building?.RemoveSelectionReason(this);
            }

            selected.Clear();
        }

        private static bool BoundsIntersectsScreenRect(Bounds bounds, Rect rect, Camera camera)
        {
            Vector3 min = bounds.min;
            Vector3 max = bounds.max;
            for (int x = 0; x < 2; x++)
            {
                for (int y = 0; y < 2; y++)
                {
                    for (int z = 0; z < 2; z++)
                    {
                        Vector3 world = new Vector3(
                            x == 0 ? min.x : max.x,
                            y == 0 ? min.y : max.y,
                            z == 0 ? min.z : max.z);
                        Vector3 screen = camera.WorldToScreenPoint(world);
                        if (screen.z > 0f && rect.Contains(new Vector2(screen.x, screen.y)))
                        {
                            return true;
                        }
                    }
                }
            }

            Vector3 center = camera.WorldToScreenPoint(bounds.center);
            return center.z > 0f && rect.Contains(new Vector2(center.x, center.y));
        }

        private Rect GetSelectionRect()
        {
            float xMin = Mathf.Min(selectionStart.x, selectionEnd.x);
            float yMin = Mathf.Min(selectionStart.y, selectionEnd.y);
            return new Rect(
                xMin,
                yMin,
                Mathf.Max(4f, Mathf.Abs(selectionEnd.x - selectionStart.x)),
                Mathf.Max(4f, Mathf.Abs(selectionEnd.y - selectionStart.y)));
        }

        internal static bool TryGetTerrainPosition(Vector2 screenPosition, out Vector3 position)
        {
            position = default(Vector3);
            Camera camera = Camera.main;
            if (camera == null)
            {
                return false;
            }

            RaycastHit[] hits = Physics.RaycastAll(
                camera.ScreenPointToRay(screenPosition),
                2000f,
                ClickHandler.defaultLayerMask);
            foreach (RaycastHit hit in hits.OrderBy(hit => hit.distance))
            {
                if (Helper.IsTerrain(hit.collider.gameObject))
                {
                    position = hit.point;
                    return true;
                }
            }

            return false;
        }

        private void OnGUI()
        {
            string hint = null;
            bool warning = false;
            if (state == MoveState.Preview && placementSession != null)
            {
                bool copying = batchKind == BatchKind.Copy;
                bool valid = placementSession.IsPreviewValid;
                int roadNodes = placementSession.RoadNodeCount;
                string title = copying ? "蓝图复制" : "批量移动";
                if (!valid)
                {
                    hint = title + "：目标位置不可用或资源不足    |    右键或 Esc 取消";
                    warning = true;
                }
                else if (copying)
                {
                    hint = $"{title}：{selected.Count} 个建筑，{roadNodes} 个路段    |    左键确认    |    {RotateHint()}    |    按住 Shift 微调    |    右键或 Esc 取消";
                }
                else
                {
                    hint = $"{title}：{selected.Count} 个建筑，{roadNodes} 个路段    |    左键确认    |    按住 Shift 微调    |    右键或 Esc 取消";
                }
            }
            else if (batchKind != BatchKind.None &&
                     WorldScripts.Inst != null &&
                     WorldScripts.Inst.dashboard.IsMoving())
            {
                hint = batchKind == BatchKind.Copy
                    ? "蓝图复制：拖框选择建筑、道路和管道    |    选框边界处自动截断"
                    : "批量移动：拖框选择建筑、道路和管道    |    选框边界处自动截断";
            }
            else if (RailChainUpgradePatch.ShowHint)
            {
                hint = "铁路链式升级：按住 Shift 升级全部连通铁路";
            }

            if (hint != null)
            {
                DrawHint(hint, warning);
            }

            if (state == MoveState.Selecting)
            {
                Rect rect = GetSelectionRect();
                Rect guiRect = new Rect(rect.x, Screen.height - rect.yMax, rect.width, rect.height);
                Color previous = GUI.color;
                GUI.color = new Color(0.2f, 0.7f, 1f, 0.22f);
                GUI.DrawTexture(guiRect, Texture2D.whiteTexture);
                GUI.color = new Color(0.35f, 0.82f, 1f, 0.95f);
                DrawBorder(guiRect, 2f);
                GUI.color = previous;
            }
        }

        private static void DrawHint(string text, bool warning)
        {
            if (hintStyle == null)
            {
                hintStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 22,
                    fontStyle = FontStyle.Bold
                };
                hintStyle.normal.textColor = Color.white;
            }

            float width = Mathf.Max(640f, hintStyle.CalcSize(new GUIContent(text)).x + 48f);
            width = Mathf.Min(width, Screen.width - 40f);
            Rect rect = new Rect((Screen.width - width) * 0.5f, 72f, width, 48f);
            Color previous = GUI.color;
            GUI.color = new Color(0.08f, 0.1f, 0.12f, 0.92f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = warning ? new Color(1f, 0.84f, 0.38f) : Color.white;
            GUI.Label(rect, text, hintStyle);
            GUI.color = previous;
        }

        private static bool IsTextInputFocused()
        {
            EventSystem current = EventSystem.current;
            if (current == null)
            {
                return false;
            }

            GameObject selected = current.currentSelectedGameObject;
            if (selected == null)
            {
                return false;
            }

            InputField input = selected.GetComponent<InputField>();
            if (input != null && input.isFocused)
            {
                return true;
            }

            Component tmpInput = selected.GetComponent("TMP_InputField");
            if (tmpInput == null)
            {
                return false;
            }

            PropertyInfo focused = tmpInput.GetType().GetProperty("isFocused");
            return focused != null && focused.GetValue(tmpInput, null) is bool isFocused && isFocused;
        }

        private static string RotateHint()
        {
            return KeyLabel(Keybinding.CameraRotateLeft) + "/" + KeyLabel(Keybinding.CameraRotateRight) + " 旋转 90°";
        }

        private static string KeyLabel(Keybinding binding)
        {
            if (SettingsHandler.GetCurrentSettings().keybindings.TryGetValue(binding, out KeyCode key))
            {
                return key.ToString();
            }

            return binding == Keybinding.CameraRotateLeft ? "Q" : "E";
        }

        private static void DrawBorder(Rect rect, float width)
        {
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, width), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.x, rect.yMax - width, rect.width, width), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.x, rect.y, width, rect.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.xMax - width, rect.y, width, rect.height), Texture2D.whiteTexture);
        }

        private void OnDestroy()
        {
            Cancel();
            if (WorldScripts.Inst != null)
            {
                WorldScripts.Inst.clickHandler?.RemovePointerListener(this);
                WorldScripts.Inst.cameraMovement?.cameraDragBlockers.Remove(this);
                WorldScripts.Inst.cameraMovement?.cameraRotationBlockers.Remove(this);
            }
        }
    }
}
