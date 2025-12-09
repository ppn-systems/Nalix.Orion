using Microsoft.Xna.Framework;

namespace Nalix.Mono.UI.Inventory;

public sealed class InventoryLayout
{
    public System.Int32 SlotSize { get; private set; }

    public System.Int32 SlotPadding { get; }

    public Rectangle PanelRect { get; private set; }

    public Vector2 GridPosition { get; private set; }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0290:Use primary constructor", Justification = "<Pending>")]
    public InventoryLayout(System.Int32 rows, System.Int32 columns)
    {
        this._rows = rows;
        this._columns = columns;
        this.SlotSize = 30;
        this.SlotPadding = 4;
    }

    public void Recalculate(System.Int32 screenWidth, System.Int32 screenHeight)
    {
        System.Int32 panelWidth = (System.Int32)(screenWidth * PanelWidthPercent) + BottomExtraPadding;
        System.Int32 panelHeight = (System.Int32)(screenHeight * PanelHeightPercent) + (BottomExtraPadding * 2);
        System.Int32 panelX = (screenWidth - panelWidth) / 2;
        System.Int32 panelY = (screenHeight - panelHeight) / 2;
        this.PanelRect = new Rectangle(panelX, panelY, panelWidth, panelHeight);
        System.Single num = panelWidth - (PanelPadding * 2);
        System.Int32 innerHeight = panelHeight - (PanelPadding * 2);
        System.Single slotWidthF = (num - ((this._columns - 1) * this.SlotPadding)) / _columns;
        System.Single slotHeightF = (innerHeight - ((this._rows - 1) * this.SlotPadding)) / (System.Single)this._rows;
        this.SlotSize = (System.Int32)System.Math.Floor((System.Double)System.Math.Min(slotWidthF, slotHeightF));
        System.Int32 gridWidth = this.GetGridWidth();
        System.Int32 gridHeight = this.GetGridHeight();
        System.Int32 gridX = panelX + ((panelWidth - gridWidth) / 2);
        System.Int32 gridY = panelY + ((panelHeight - gridHeight) / 2);
        this.GridPosition = new Vector2(gridX, gridY);
    }

    public System.Int32 GetGridWidth()
    {
        return (this._columns * this.SlotSize) + ((this._columns - 1) * this.SlotPadding);
    }

    public System.Int32 GetGridHeight()
    {
        return (this._rows * this.SlotSize) + ((this._rows - 1) * this.SlotPadding);
    }

    public Rectangle GetSlotRectangle(System.Int32 row, System.Int32 column)
    {
        System.Int32 num = (System.Int32)(this.GridPosition.X + (column * (this.SlotSize + this.SlotPadding)));
        System.Single y = this.GridPosition.Y + (row * (this.SlotSize + this.SlotPadding));

        if (row == _rows - 1) // hàng cuối
        {
            y += BottomExtraPadding;
        }

        return new Rectangle(num, (System.Int32)y, this.SlotSize, this.SlotSize);
    }

    public Point HitTest(Point mousePosition)
    {
        for (System.Int32 row = 0; row < this._rows; row++)
        {
            for (System.Int32 col = 0; col < this._columns; col++)
            {
                if (this.GetSlotRectangle(row, col).Contains(mousePosition))
                {
                    return new Point(row, col);
                }
            }
        }
        return new Point(-1, -1);
    }

    private readonly System.Int32 _rows;

    private readonly System.Int32 _columns;

    private const System.Int32 PanelPadding = 24;
    private const System.Int32 BottomExtraPadding = 12;
    private const System.Single PanelWidthPercent = 0.45f;
    private const System.Single PanelHeightPercent = 0.40f;

}
