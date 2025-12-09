using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Nalix.Domain.Inventory;
using Nalix.Domain.Items;
using Nalix.Mono.UI.Rendering;
using System.Collections.Generic;
using System.Globalization;

namespace Nalix.Mono.UI.Inventory;

public sealed class InventoryView
{
    public Vector2 Position
    {
        get
        {
            return this._layout.GridPosition;
        }
    }

    public System.Int32 GridWidth
    {
        get
        {
            return this._layout.GetGridWidth();
        }
    }

    public System.Int32 GridHeight
    {
        get
        {
            return this._layout.GetGridHeight();
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Maintainability", "CA1510:Use ArgumentNullException throw helper", Justification = "<Pending>")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0016:Use 'throw' expression", Justification = "<Pending>")]
    public InventoryView(
        InventoryGrid inventory, SpriteBatch spriteBatch,
        Texture2D slotBackgroundTexture, SpriteFont font,
        IReadOnlyDictionary<System.String, Texture2D> iconMap, Texture2D panelBackgroundTexture = null)
    {
        if (inventory == null)
        {
            throw new System.ArgumentNullException(nameof(inventory));
        }
        this._inventory = inventory;
        if (spriteBatch == null)
        {
            throw new System.ArgumentNullException(nameof(spriteBatch));
        }
        this._spriteBatch = spriteBatch;
        if (slotBackgroundTexture == null)
        {
            throw new System.ArgumentNullException(nameof(slotBackgroundTexture));
        }
        this._slotBackgroundTexture = slotBackgroundTexture;
        if (font == null)
        {
            throw new System.ArgumentNullException(nameof(font));
        }
        this._font = font;
        if (iconMap == null)
        {
            throw new System.ArgumentNullException(nameof(iconMap));
        }
        this._iconMap = iconMap;
        if (panelBackgroundTexture != null)
        {
            this._panelBackground = new NineSliceTexture(panelBackgroundTexture);
        }
        this._layout = new InventoryLayout(inventory.Rows, inventory.Columns);
        this._interaction = new InventoryActions(inventory);
    }

    public void CenterOnScreen(System.Int32 screenWidth, System.Int32 screenHeight)
    {
        _layout.Recalculate(screenWidth, screenHeight);
        RecalculateCharacterLayout();
    }

    public void Update(GameTime gameTime, MouseState mouseState)
    {
        this._lastMousePosition = mouseState.Position;
        this._hoveredSlot = this._layout.HitTest(mouseState.Position);
        System.Boolean leftClicked = mouseState.LeftButton == ButtonState.Pressed && this._previousMouseState.LeftButton == ButtonState.Released;
        System.Boolean rightClicked = mouseState.RightButton == ButtonState.Pressed && this._previousMouseState.RightButton == ButtonState.Released;
        if (this._hoveredSlot.X >= 0 && this._hoveredSlot.Y >= 0)
        {
            if (leftClicked)
            {
                System.Double nowMs = gameTime.TotalGameTime.TotalMilliseconds;
                this._interaction.OnLeftClick(this._hoveredSlot.X, this._hoveredSlot.Y, nowMs);
            }
            else if (rightClicked)
            {
                this._interaction.OnRightClick(this._hoveredSlot.X, this._hoveredSlot.Y);
            }
        }
        this._previousMouseState = mouseState;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0031:Use null propagation", Justification = "<Pending>")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Roslynator", "RCS1146:Use conditional access", Justification = "<Pending>")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0018:Inline variable declaration", Justification = "<Pending>")]
    public void Draw(GameTime _)
    {
        Rectangle panelRect = this._layout.PanelRect;
        System.Int32 slotSize = this._layout.SlotSize;
        NineSliceTexture panelBackground = this._panelBackground;
        if (panelBackground != null)
        {
            panelBackground.Draw(this._spriteBatch, panelRect, new Color(8, 10, 18, 235));
        }
        Rectangle innerRect = new(panelRect.X + 4, panelRect.Y + 4, panelRect.Width - 8, panelRect.Height - 8);
        NineSliceTexture panelBackground2 = this._panelBackground;
        if (panelBackground2 != null)
        {
            panelBackground2.Draw(this._spriteBatch, innerRect, new Color(30, 36, 52, 70));
        }
        for (System.Int32 row = 0; row < this._inventory.Rows; row++)
        {
            for (System.Int32 col = 0; col < this._inventory.Columns; col++)
            {
                Rectangle slotRect = this._layout.GetSlotRectangle(row, col);
                Color baseSlotColor = new(16, 18, 26, 255);
                this._spriteBatch.Draw(this._slotBackgroundTexture, slotRect, baseSlotColor);
                ItemStack stack = this._inventory.GetSlot(row, col);
                if (stack != null)
                {
                    this._spriteBatch.Draw(this._slotBackgroundTexture, slotRect, InventoryView.GetRarityColor(stack));
                }
                if (this._hoveredSlot.X == row && this._hoveredSlot.Y == col)
                {
                    Color hoverColor = new(230, 245, 255, 90);
                    this._spriteBatch.Draw(this._slotBackgroundTexture, slotRect, hoverColor);
                }
                if (stack != null)
                {
                    Texture2D iconTexture;
                    if (this._iconMap.TryGetValue(stack.Definition.IconKey, out iconTexture))
                    {
                        System.Int32 iconSize = slotSize - 10;
                        Vector2 iconPos = new(slotRect.X + ((slotRect.Width - iconSize) / 2), slotRect.Y + ((slotRect.Height - iconSize) / 2));
                        Rectangle iconRect = new((System.Int32)iconPos.X, (System.Int32)iconPos.Y, iconSize, iconSize);
                        this._spriteBatch.Draw(iconTexture, iconRect, Color.White);
                    }
                    if (stack.Quantity > 1)
                    {
                        System.String text = stack.Quantity.ToString(CultureInfo.InvariantCulture);
                        Vector2 textSize = this._font.MeasureString(text);
                        Vector2 textPos = new(slotRect.Right - textSize.X - 2f, slotRect.Bottom - textSize.Y + 2f);
                        this.DrawOutlinedText(text, textPos, new Color(255, 230, 160));
                    }
                }
            }
        }

        this.DrawCursorStack();
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0066:Convert switch statement to expression", Justification = "<Pending>")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Roslynator", "RCS1179:Unnecessary assignment", Justification = "<Pending>")]
    private static Color GetRarityColor(ItemStack stack)
    {
        Color color;
        switch (stack.Definition.Rarity)
        {
            case ItemRarity.Common:
                color = new Color(120, 120, 120, 60);
                break;
            case ItemRarity.Uncommon:
                color = new Color(80, 200, 120, 60);
                break;
            case ItemRarity.Rare:
                color = new Color(80, 140, 255, 60);
                break;
            case ItemRarity.Epic:
                color = new Color(180, 90, 255, 60);
                break;
            case ItemRarity.Legendary:
                color = new Color(255, 190, 70, 60);
                break;
            default:
                color = new Color(140, 140, 140);
                break;
        }
        return color;
    }

    private void DrawOutlinedText(System.String text, Vector2 position, Color mainColor)
    {
        Color outlineColor = new(0, 0, 0, 210);
        this._spriteBatch.DrawString(this._font, text, position + new Vector2(-1f, 0f), outlineColor);
        this._spriteBatch.DrawString(this._font, text, position + new Vector2(1f, 0f), outlineColor);
        this._spriteBatch.DrawString(this._font, text, position + new Vector2(0f, -1f), outlineColor);
        this._spriteBatch.DrawString(this._font, text, position + new Vector2(0f, 1f), outlineColor);
        this._spriteBatch.DrawString(this._font, text, position, mainColor);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0018:Inline variable declaration", Justification = "<Pending>")]
    private void DrawCursorStack()
    {
        ItemStack cursorStack = this._interaction.CursorStack;
        if (cursorStack == null)
        {
            return;
        }
        System.Int32 slotSize = this._layout.SlotSize;
        Point mouse = this._lastMousePosition;
        Rectangle cursorRect = new(mouse.X - (slotSize / 2), mouse.Y - ((slotSize / 2) - 4), slotSize, slotSize);
        Texture2D iconTexture;
        if (this._iconMap.TryGetValue(cursorStack.Definition.IconKey, out iconTexture))
        {
            this._spriteBatch.Draw(iconTexture, cursorRect, new Color(0, 0, 0, 120));
            this._spriteBatch.Draw(iconTexture, cursorRect, Color.White);
        }
        if (cursorStack.Quantity > 1)
        {
            System.String text = cursorStack.Quantity.ToString(CultureInfo.InvariantCulture);
            Vector2 textSize = this._font.MeasureString(text);
            Vector2 textPos = new(cursorRect.Right - textSize.X, cursorRect.Bottom - textSize.Y);
            this._spriteBatch.DrawString(this._font, text, textPos, Color.White);
        }
    }

    private void RecalculateCharacterLayout()
    {
        var panelRect = _layout.PanelRect;
        System.Int32 slotSize = _layout.SlotSize;

        const System.Int32 margin = 10;           // khoảng cách từ viền panel
        const System.Int32 gap = 4;               // khoảng cách giữa các slot giáp

        // Khung nhân vật (2x3 slot size, bạn chỉnh theo ý thích)
        _characterRect = new Rectangle(
            panelRect.X + margin + slotSize,  // đẩy sang phải 1 slot để còn chỗ cho cột giáp
            panelRect.Y + margin,
            slotSize * 2,
            slotSize * 3
        );

        // 4 ô giáp xếp dọc, bên trái khung nhân vật
        System.Int32 armorX = panelRect.X + margin;
        System.Int32 armorY = _characterRect.Y;

        for (System.Int32 i = 0; i < _armorSlotRects.Length; i++)
        {
            _armorSlotRects[i] = new Rectangle(
                armorX,
                armorY + (i * (slotSize + gap)),
                slotSize,
                slotSize
            );
        }
    }

    private readonly SpriteFont _font;

    private readonly InventoryGrid _inventory;

    private readonly SpriteBatch _spriteBatch;

    private readonly Texture2D _slotBackgroundTexture;

    private readonly IReadOnlyDictionary<System.String, Texture2D> _iconMap;

    private readonly InventoryLayout _layout;

    private readonly InventoryActions _interaction;

    private readonly NineSliceTexture _panelBackground;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0090:Use 'new(...)'", Justification = "<Pending>")]
    private Point _hoveredSlot = new Point(-1, -1);

    private MouseState _previousMouseState;

    private Point _lastMousePosition;

    private Rectangle _characterRect;
    private readonly Rectangle[] _armorSlotRects = new Rectangle[4];
}
