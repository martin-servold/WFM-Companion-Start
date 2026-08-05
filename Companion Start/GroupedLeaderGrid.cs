using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace CompanionStart
{
    // CardContainerGrid lays every card out as one contiguous block - GetChildPosition derives
    // row/column purely from each card's index in the "entities" list, and the private SetSize
    // it calls on add/remove sizes the grid the same way. To show the companions shared between
    // multiple clans as a visually separate cluster (with a blank row between them and the rest),
    // that same math needs to run independently per group, offsetting the second group's rows by
    // however many the first group used. The private fields it depends on (cellSize, spacing,
    // columnCount) aren't visible to a subclass, so they're read once via Traverse - the same
    // pattern LeaderSelectionPatch already uses elsewhere in this mod for reading privates off
    // vanilla components - rather than duplicating their default values here.
    //
    // This is instantiated for every clan's leader-select grid, not just this mod's companion
    // clans (see LeaderSelectionPatch.ConvertToScrollableGrid). For a vanilla clan none of its
    // leaders match SharedCompanionTitles, so the "shared" group is empty and every card falls
    // into the single "rest" group - identical to plain CardContainerGrid.
    internal class GroupedLeaderGrid : CardContainerGrid
    {
        private static readonly HashSet<string> SharedCompanionTitles = new HashSet<string>(new[]
        {
            "Big Berry", "Blunky", "Bombom", "Bonnie", "Dimona", "Foxee", "Gojiber",
            "Jumbo", "Lupa", "Nova", "Roibos", "Snobble", "Snoffel"
        }, StringComparer.OrdinalIgnoreCase);

        // Expressed as a fraction of one row's pitch (cellSize + spacing) rather than a whole
        // extra row - GetChildPosition's row/column math collapses to a constant per-row pitch
        // (see its derivation from vanilla's formula), so a fractional row offset here still
        // lands exactly proportionally between rows instead of requiring a whole blank one.
        private const float GapFraction = 0.5f;

        // Named distinctly from the base class's own private "cellSize"/"spacing"/"columnCount"
        // fields - reusing those names here would declare separate shadow fields of the same
        // name, and Traverse.Field(name) below would then read back these (still-zero) shadows
        // instead of the base's real values.
        private Vector2 gridCellSize;
        private Vector2 gridSpacing;
        private int gridColumnCount;

        private void Awake()
        {
            Traverse traverse = Traverse.Create(this);
            gridCellSize = traverse.Field("cellSize").GetValue<Vector2>();
            gridSpacing = traverse.Field("spacing").GetValue<Vector2>();
            gridColumnCount = traverse.Field("columnCount").GetValue<int>();
        }

        private static bool IsShared(Entity entity)
        {
            return SharedCompanionTitles.Contains(entity.data.title);
        }

        private List<Entity> SharedEntities()
        {
            return this.Where(IsShared).ToList();
        }

        private List<Entity> RestEntities()
        {
            return this.Where(e => !IsShared(e)).ToList();
        }

        private int RowsUsedBy(int count)
        {
            return gridColumnCount <= 0 ? 0 : Mathf.CeilToInt((float)count / gridColumnCount);
        }

        private static int RowCountInGroup(int groupCount, int columnCount, int rowIndex)
        {
            return Mathf.Clamp(groupCount - rowIndex * columnCount, 0, columnCount);
        }

        public override Vector3 GetChildPosition(Entity child)
        {
            List<Entity> shared = SharedEntities();
            bool isShared = shared.Contains(child);
            List<Entity> group = isShared ? shared : RestEntities();
            int localIndex = group.IndexOf(child);
            int col = localIndex % gridColumnCount;
            int localRow = localIndex / gridColumnCount;
            int rowSize = RowCountInGroup(group.Count, gridColumnCount, localRow);
            float rowOffset = isShared ? 0f : RowsUsedBy(shared.Count) + (shared.Count > 0 ? GapFraction : 0f);
            float row = localRow + rowOffset;

            float rowWidth = rowSize * gridCellSize.x + (rowSize - 1) * gridSpacing.x;
            Vector2 sizeDelta = base.rectTransform.sizeDelta;
            Vector2 position = new Vector2(0f - sizeDelta.x, sizeDelta.y) * 0.5f;
            position.x = (0f - rowWidth) * 0.5f;
            position.x += gridCellSize.x * 0.5f + gridSpacing.x;
            position.y -= gridCellSize.y * 0.5f + gridSpacing.y;

            position.x += col * gridCellSize.x + (col - 1) * gridSpacing.x;
            position.y -= row * gridCellSize.y + (row - 1) * gridSpacing.y;
            return position;
        }

        protected override void CardAdded(Entity entity)
        {
            base.CardAdded(entity);
            FixSize();
        }

        protected override void CardRemoved(Entity entity)
        {
            base.CardRemoved(entity);
            FixSize();
        }

        // Re-derives what the base class's private SetSize() just computed from a single item
        // count, which undercounts once a blank row is inserted between the two groups.
        private void FixSize()
        {
            int sharedCount = SharedEntities().Count;
            int restCount = Count - sharedCount;
            float totalRows = RowsUsedBy(sharedCount) + (sharedCount > 0 && restCount > 0 ? GapFraction : 0f) + RowsUsedBy(restCount);
            int columns = Mathf.Min(gridColumnCount, Count);
            float width = columns * gridCellSize.x + Mathf.Max(0, columns - 1) * gridSpacing.x;
            float height = totalRows * gridCellSize.y + Mathf.Max(0, totalRows - 1) * gridSpacing.y;

            LayoutElement layoutElement = GetComponent<LayoutElement>();
            if (layoutElement != null)
            {
                layoutElement.preferredWidth = width;
                layoutElement.preferredHeight = height;
            }
            else
            {
                base.rectTransform.sizeDelta = new Vector2(width, height);
            }
        }
    }
}
