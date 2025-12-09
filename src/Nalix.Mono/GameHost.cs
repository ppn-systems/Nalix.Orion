using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Nalix.Domain.Inventory;
using Nalix.Domain.Items;
using Nalix.Mono.UI.Inventory;
using System.Collections.Generic;

namespace Nalix.Mono;

public class GameHost : Game
{
    private readonly GraphicsDeviceManager _graphics;
    private SpriteBatch _spriteBatch;

    private InventoryGrid _inventory = null!;
    private InventoryView _inventoryView = null!;

    private Texture2D _slotTexture = null!;
    private SpriteFont _font = null!;
    private Dictionary<System.String, Texture2D> _iconMap = null!;

    public GameHost()
    {
        _graphics = new GraphicsDeviceManager(this)
        {
            IsFullScreen = false,
            PreferredBackBufferWidth = 1280,
            PreferredBackBufferHeight = 720,
            SynchronizeWithVerticalRetrace = false
        };

        IsMouseVisible = true;
        Content.RootDirectory = "Content";
    }

    protected override void Initialize()
    {
        // TODO: Add your initialization logic here

        base.Initialize();
    }

    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);

        _slotTexture = Content.Load<Texture2D>("ui/panels/015");
        Texture2D panelTex = Content.Load<Texture2D>("ui/panels/004");
        _font = Content.Load<SpriteFont>("fonts/1");

        _iconMap = new Dictionary<System.String, Texture2D>
        {
            ["item.rada1"] = Content.Load<Texture2D>("items/433"),
            ["item.rada4"] = Content.Load<Texture2D>("items/434")
        };

        _inventory = new InventoryGrid(rows: 4, columns: 9);
        ItemDefinition appleDef = new("433", "Apple", "item.rada1");
        ItemDefinition appleDef2 = new("433", "Apple", "item.rada4", rarity: ItemRarity.Epic);
        _inventory.SetSlot(0, 0, new ItemStack(appleDef, 64));
        _inventory.SetSlot(0, 1, new ItemStack(appleDef2, 14));

        _inventoryView = new InventoryView(
            _inventory,
            _spriteBatch,
            _slotTexture,
            _font,
            _iconMap,
            panelBackgroundTexture: panelTex);

        _inventoryView.CenterOnScreen(GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);
    }

    protected override void Update(GameTime gameTime)
    {
        if (GamePad.GetState(PlayerIndex.One).Buttons.Back == ButtonState.Pressed || Keyboard.GetState().IsKeyDown(Keys.Escape))
        {
            Exit();
        }

        _inventoryView.Update(gameTime, Mouse.GetState());

        // TODO: Add your update logic here

        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(Color.CornflowerBlue);

        _spriteBatch.Begin(samplerState: SamplerState.PointClamp);
        _inventoryView.Draw(gameTime);
        _spriteBatch.End();

        base.Draw(gameTime);
    }
}
