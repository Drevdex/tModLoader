using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Audio;
using ReLogic.Graphics;
using Terraria.ID;
using Terraria.ModLoader.Exceptions;
using Terraria.ModLoader.IO;
using Terraria.Audio;
using Terraria.ModLoader.Audio;
using Terraria.Localization;
using log4net;

namespace Terraria.ModLoader
{
	/// <summary>
	/// Mod is an abstract class that you will override. It serves as a central place from which the mod's contents are stored. It provides methods for you to use or override.
	/// </summary>
	public abstract partial class Mod
	{
		/// <summary>
		/// The TmodFile object created when tModLoader reads this mod.
		/// </summary>
		public TmodFile File { get; internal set; }
		/// <summary>
		/// The assembly code this is loaded when tModLoader loads this mod.
		/// </summary>
		public Assembly Code { get; internal set; }
		/// <summary>
		/// A logger with this mod's name for easy logging.
		/// </summary>
		public ILog Logger { get; internal set; }

		/// <summary>
		/// Stores the name of the mod. This name serves as the mod's identification, and also helps with saving everything your mod adds. By default this returns the name of the folder that contains all your code and stuff.
		/// </summary>
		public virtual string Name => File.name;
		/// <summary>
		/// The version of tModLoader that was being used when this mod was built.
		/// </summary>
		public virtual Version tModLoaderVersion => File.tModLoaderVersion;
		/// <summary>
		/// This version number of this mod.
		/// </summary>
		public virtual Version Version => File.version;

		public ModProperties Properties { get; protected set; } = ModProperties.AutoLoadAll;
		/// <summary>
		/// The ModSide that controls how this mod is synced between client and server.
		/// </summary>
		public ModSide Side { get; internal set; }
		/// <summary>
		/// The display name of this mod in the Mods menu.
		/// </summary>
		public string DisplayName { get; internal set; }

		internal short netID = -1;
		public bool IsNetSynced => netID >= 0;


		/// <summary>
		/// Override this method to add most of your content to your mod. Here you will call other methods such as AddItem. This is guaranteed to be called after all content has been autoloaded.
		/// </summary>
		public virtual void Load()
		{
		}

		/// <summary>
		/// Allows you to load things in your mod after its content has been setup (arrays have been resized to fit the content, etc).
		/// </summary>
		public virtual void PostSetupContent()
		{
		}

		/// <summary>
		/// This is called whenever this mod is unloaded from the game. Use it to undo changes that you've made in Load that aren't automatically handled (for example, modifying the texture of a vanilla item). Mods are guaranteed to be unloaded in the reverse order they were loaded in.
		/// </summary>
		public virtual void Unload()
		{
		}

		/// <summary>
		/// Override this method to add recipe groups to this mod. You must add recipe groups by calling the RecipeGroup.RegisterGroup method here. A recipe group is a set of items that can be used interchangeably in the same recipe.
		/// </summary>
		public virtual void AddRecipeGroups()
		{
		}

		/// <summary>
		/// Override this method to add recipes to the game. It is recommended that you do so through instances of ModRecipe, since it provides methods that simplify recipe creation.
		/// </summary>
		public virtual void AddRecipes()
		{
		}

		/// <summary>
		/// This provides a hook into the mod-loading process immediately after recipes have been added. You can use this to edit recipes added by other mods.
		/// </summary>
		public virtual void PostAddRecipes()
		{
		}

		public virtual void LoadResourceFromStream(string path, int len, BinaryReader reader)
		{
			if (Main.dedServ)
				return;

			Interface.loadMods.SubProgressText = path;

			string extension = Path.GetExtension(path);
			path = Path.ChangeExtension(path, null);
			switch (extension)
			{
				case ".png":
				case ".rawimg":
					//png files need a seekable stream
					LoadTexture(path, len, reader, extension == ".rawimg");
					return;
				case ".wav":
					LoadWav(path, reader.ReadBytes(len));
					return;
				case ".mp3":
					LoadMP3(path, reader.ReadBytes(len));
					return;
				case ".xnb":
					if (path.StartsWith("Fonts/"))
					{
						LoadFont(path, reader.ReadBytes(len));
						return;
					}
					if (path.StartsWith("Effects/"))
					{
						LoadEffect(path, reader);
						return;
					}
					throw new ResourceLoadException(Language.GetTextValue("tModLoader.LoadErrorUnknownXNBFileHint", path));
			}

			throw new ResourceLoadException($"Unknown streaming asset {path}{extension}. ");
		}

		/// <summary>
		/// Adds a type of item to your mod with the specified internal name. This method should be called in Load. You can obtain an instance of ModItem by overriding it then creating an instance of the subclass.
		/// </summary>
		/// <param name="name">The name.</param>
		/// <param name="item">The item.</param>
		/// <exception cref="System.Exception">You tried to add 2 ModItems with the same name: " + name + ". Maybe 2 classes share a classname but in different namespaces while autoloading or you manually called AddItem with 2 items of the same name.</exception>
		public void AddItem(string name, ModItem item)
		{
			if (!loading)
				throw new Exception(Language.GetTextValue("tModLoader.LoadErrorAddItemOnlyInLoad"));

			if (items.ContainsKey(name))
				throw new Exception(Language.GetTextValue("tModLoader.LoadError2ModItemSameName", name));

			item.mod = this;
			item.Name = name;
			item.DisplayName = GetOrCreateTranslation(string.Format("Mods.{0}.ItemName.{1}", Name, name));
			item.Tooltip = GetOrCreateTranslation(string.Format("Mods.{0}.ItemTooltip.{1}", Name, name), true);

			item.item.ResetStats(ItemLoader.ReserveItemID());
			item.item.modItem = item;

			items[name] = item;
			ItemLoader.items.Add(item);
		}

		/// <summary>
		/// Gets the ModItem instance corresponding to the name. Because this method is in the Mod class, conflicts between mods are avoided. Returns null if no ModItem with the given name is found.
		/// </summary>
		/// <param name="name">The name.</param>
		/// <returns></returns>
		public ModItem GetItem(string name) => items.TryGetValue(name, out var item) ? item : null;

		/// <summary>
		/// Same as the other GetItem, but assumes that the class name and internal name are the same.
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <returns></returns>
		public T GetItem<T>() where T : ModItem => (T)GetItem(typeof(T).Name);

		/// <summary>
		/// Gets the internal ID / type of the ModItem corresponding to the name. Returns 0 if no ModItem with the given name is found.
		/// </summary>
		/// <param name="name">The name.</param>
		/// <returns></returns>
		public int ItemType(string name) => GetItem(name)?.item.type ?? 0;

		/// <summary>
		/// Same as the other ItemType, but assumes that the class name and internal name are the same.
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <returns></returns>
		public int ItemType<T>() where T : ModItem => ItemType(typeof(T).Name);

		/// <summary>
		/// Adds the given GlobalItem instance to this mod with the provided name.
		/// </summary>
		/// <param name="name">The name.</param>
		/// <param name="globalItem">The global item.</param>
		public void AddGlobalItem(string name, GlobalItem globalItem)
		{
			if (!loading)
				throw new Exception("AddGlobalItem can only be called from Mod.Load or Mod.Autoload");

			ItemLoader.VerifyGlobalItem(globalItem);

			globalItem.mod = this;
			globalItem.Name = name;

			globalItems[name] = globalItem;
			globalItem.index = ItemLoader.globalItems.Count;
			ItemLoader.globalIndexes[Name + ':' + name] = ItemLoader.globalItems.Count;
			if (ItemLoader.globalIndexesByType.ContainsKey(globalItem.GetType()))
			{
				ItemLoader.globalIndexesByType[globalItem.GetType()] = -1;
			}
			else
			{
				ItemLoader.globalIndexesByType[globalItem.GetType()] = ItemLoader.globalItems.Count;
			}
			ItemLoader.globalItems.Add(globalItem);
		}

		/// <summary>
		/// Gets the GlobalItem instance with the given name from this mod.
		/// </summary>
		/// <param name="name">The name.</param>
		/// <returns></returns>
		public GlobalItem GetGlobalItem(string name) => globalItems.TryGetValue(name, out var globalItem) ? globalItem : null;

		/// <summary>
		/// Same as the other GetGlobalItem, but assumes that the class name and internal name are the same.
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <returns></returns>
		public T GetGlobalItem<T>() where T : GlobalItem => (T)GetGlobalItem(typeof(T).Name);

		/// <summary>
		/// Adds an equipment texture of the specified type, internal name, and associated item to your mod. 
		/// (The item parameter may be null if you don't want to associate an item with the texture.) 
		/// You can then get the ID for your texture by calling EquipLoader.GetEquipTexture, and using the EquipTexture's Slot property. 
		/// If the EquipType is EquipType.Body, make sure that you also provide an armTexture and a femaleTexture. 
		/// Returns the ID / slot that is assigned to the equipment texture.
		/// </summary>
		/// <param name="item">The item.</param>
		/// <param name="type">The type.</param>
		/// <param name="name">The name.</param>
		/// <param name="texture">The texture.</param>
		/// <param name="armTexture">The arm texture (for body slots).</param>
		/// <param name="femaleTexture">The female texture (for body slots), if missing the regular body texture is used.</param>
		/// <returns></returns>
		public int AddEquipTexture(ModItem item, EquipType type, string name, string texture,
			string armTexture = "", string femaleTexture = "")
		{
			return AddEquipTexture(new EquipTexture(), item, type, name, texture, armTexture, femaleTexture);
		}

		/// <summary>
		/// Adds an equipment texture of the specified type, internal name, and associated item to your mod. 
		/// This method is different from the other AddEquipTexture in that you can specify the class of the equipment texture, thus allowing you to override EquipmentTexture's hooks. 
		/// All other parameters are the same as the other AddEquipTexture.
		/// </summary>
		/// <param name="equipTexture">The equip texture.</param>
		/// <param name="item">The item.</param>
		/// <param name="type">The type.</param>
		/// <param name="name">The name.</param>
		/// <param name="texture">The texture.</param>
		/// <param name="armTexture">The arm texture (for body slots).</param>
		/// <param name="femaleTexture">The female texture (for body slots), if missing the regular body texture is used.</param>
		/// <returns></returns>
		public int AddEquipTexture(EquipTexture equipTexture, ModItem item, EquipType type, string name, string texture,
			string armTexture = "", string femaleTexture = "")
		{
			if (!loading)
				throw new Exception("AddEquipTexture can only be called from Mod.Load or Mod.Autoload");

			ModContent.GetTexture(texture); //ensure texture exists

			equipTexture.Texture = texture;
			equipTexture.mod = this;
			equipTexture.Name = name;
			equipTexture.Type = type;
			equipTexture.item = item;
			int slot = equipTexture.Slot = EquipLoader.ReserveEquipID(type);

			EquipLoader.equipTextures[type][slot] = equipTexture;
			equipTextures[Tuple.Create(name, type)] = equipTexture;

			if (type == EquipType.Body)
			{
				if (femaleTexture == null || !ModContent.TextureExists(femaleTexture))
					femaleTexture = texture;
				EquipLoader.femaleTextures[slot] = femaleTexture;

				ModContent.GetTexture(armTexture); //ensure texture exists
				EquipLoader.armTextures[slot] = armTexture;
			}
			if (item != null)
			{
				IDictionary<EquipType, int> slots;
				if (!EquipLoader.idToSlot.TryGetValue(item.item.type, out slots))
					EquipLoader.idToSlot[item.item.type] = slots = new Dictionary<EquipType, int>();

				slots[type] = slot;
				if (type == EquipType.Head || type == EquipType.Body || type == EquipType.Legs)
					EquipLoader.slotToId[type][slot] = item.item.type;
			}
			return slot;
		}

		/// <summary>
		/// Gets the EquipTexture instance corresponding to the name and EquipType. Returns null if no EquipTexture with the given name and EquipType is found.
		/// </summary>
		/// <param name="name">The name.</param>
		/// <param name="type">The type.</param>
		/// <returns></returns>
		public EquipTexture GetEquipTexture(string name, EquipType type) => 
			equipTextures.TryGetValue(Tuple.Create(name, type), out var texture) ? texture : null;

		/// <summary>
		/// Gets the slot/ID of the equipment texture corresponding to the given name. Returns -1 if no EquipTexture with the given name is found.
		/// </summary>
		/// <param name="name">The name.</param>
		/// <param name="type"></param>
		/// <returns></returns>
		public int GetEquipSlot(string name, EquipType type) => GetEquipTexture(name, type)?.Slot ?? -1;

		/// <summary>
		/// Same as GetEquipSlot, except returns the number as an sbyte (signed byte) for your convenience.
		/// </summary>
		/// <param name="name">The name.</param>
		/// <param name="type"></param>
		/// <returns></returns>
		public sbyte GetAccessorySlot(string name, EquipType type) => (sbyte)GetEquipSlot(name, type);

		/// <summary>
		/// Adds a prefix to your mod with the specified internal name. This method should be called in Load. You can obtain an instance of ModPrefix by overriding it then creating an instance of the subclass.
		/// </summary>
		/// <param name="name">The name.</param>
		/// <param name="prefix">The prefix.</param>
		/// <exception cref="System.Exception">You tried to add 2 ModItems with the same name: " + name + ". Maybe 2 classes share a classname but in different namespaces while autoloading or you manually called AddItem with 2 items of the same name.</exception>
		public void AddPrefix(string name, ModPrefix prefix)
		{
			if (!loading)
				throw new Exception("AddPrefix can only be called from Mod.Load or Mod.Autoload");

			if (prefixes.ContainsKey(name))
				throw new Exception("You tried to add 2 ModPrefixes with the same name: " + name + ". Maybe 2 classes share a classname but in different namespaces while autoloading or you manually called AddPrefix with 2 prefixes of the same name.");

			prefix.mod = this;
			prefix.Name = name;
			prefix.DisplayName = GetOrCreateTranslation(string.Format("Mods.{0}.Prefix.{1}", Name, name));
			prefix.Type = ModPrefix.ReservePrefixID();

			prefixes[name] = prefix;
			ModPrefix.prefixes.Add(prefix);
			ModPrefix.categoryPrefixes[prefix.Category].Add(prefix);
		}

		/// <summary>
		/// Gets the ModPrefix instance corresponding to the name. Because this method is in the Mod class, conflicts between mods are avoided. Returns null if no ModPrefix with the given name is found.
		/// </summary>
		/// <param name="name">The name.</param>
		/// <returns></returns>
		public ModPrefix GetPrefix(string name) => prefixes.TryGetValue(name, out var prefix) ? prefix : null;

		/// <summary>
		/// Same as the other GetPrefix, but assumes that the class name and internal name are the same.
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <returns></returns>
		public T GetPrefix<T>() where T : ModPrefix => (T)GetPrefix(typeof(T).Name);

		/// <summary>
		/// Gets the internal ID / type of the ModPrefix corresponding to the name. Returns 0 if no ModPrefix with the given name is found.
		/// </summary>
		/// <param name="name">The name.</param>
		/// <returns></returns>
		public byte PrefixType(string name) => GetPrefix(name)?.Type ?? 0;

		/// <summary>
		/// Same as the other PrefixType, but assumes that the class name and internal name are the same.
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <returns></returns>
		public byte PrefixType<T>() where T : ModPrefix => PrefixType(typeof(T).Name);

		/// <summary>
		/// Adds a type of dust to your mod with the specified name. Create an instance of ModDust normally, preferably through the constructor of an overriding class. Leave the texture as an empty string to use the vanilla dust sprite sheet.
		/// </summary>
		/// <param name="name">The name.</param>
		/// <param name="dust">The dust.</param>
		/// <param name="texture">The texture.</param>
		public void AddDust(string name, ModDust dust, string texture = "")
		{
			if (!loading)
				throw new Exception("AddDust can only be called from Mod.Load or Mod.Autoload");

			dust.mod = this;
			dust.Name = name;
			dust.Type = ModDust.ReserveDustID();
			dust.Texture = !string.IsNullOrEmpty(texture) ? ModContent.GetTexture(texture) : Main.dustTexture;

			dusts[name] = dust;
			ModDust.dusts.Add(dust);
		}

		/// <summary>
		/// Gets the ModDust of this mod corresponding to the given name. Returns null if no ModDust with the given name is found.
		/// </summary>
		/// <param name="name">The name.</param>
		/// <returns></returns>
		public ModDust GetDust(string name) => dusts.TryGetValue(name, out var dust) ? dust : null;

		/// <summary>
		/// Same as the other GetDust, but assumes that the class name and internal name are the same.
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <returns></returns>
		public T GetDust<T>() where T : ModDust => (T)GetDust(typeof(T).Name);

		/// <summary>
		/// Gets the type of the ModDust of this mod with the given name. Returns 0 if no ModDust with the given name is found.
		/// </summary>
		/// <param name="name">The name.</param>
		/// <returns></returns>
		public int DustType(string name) => GetDust(name)?.Type ?? 0;

		/// <summary>
		/// Same as the other DustType, but assumes that the class name and internal name are the same.
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <returns></returns>
		public int DustType<T>() where T : ModDust => DustType(typeof(T).Name);

		/// <summary>
		/// Adds a type of tile to the game with the specified name and texture.
		/// </summary>
		/// <param name="name">The name.</param>
		/// <param name="tile">The tile.</param>
		/// <param name="texture">The texture.</param>
		public void AddTile(string name, ModTile tile, string texture)
		{
			if (!loading)
				throw new Exception("AddItem can only be called from Mod.Load or Mod.Autoload");

			if (tiles.ContainsKey(name))
				throw new Exception("You tried to add 2 ModTile with the same name: " + name + ". Maybe 2 classes share a classname but in different namespaces while autoloading or you manually called AddTile with 2 tiles of the same name.");

			tile.mod = this;
			tile.Name = name;
			tile.Type = (ushort)TileLoader.ReserveTileID();
			tile.texture = texture;

			tiles[name] = tile;
			TileLoader.tiles.Add(tile);
		}

		/// <summary>
		/// Gets the ModTile of this mod corresponding to the given name. Returns null if no ModTile with the given name is found.
		/// </summary>
		/// <param name="name">The name.</param>
		/// <returns></returns>
		public ModTile GetTile(string name) => tiles.TryGetValue(name, out var tile) ? tile : null;

		/// <summary>
		/// Same as the other GetTile, but assumes that the class name and internal name are the same.
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <returns></returns>
		public T GetTile<T>() where T : ModTile => (T)GetTile(typeof(T).Name);

		/// <summary>
		/// Gets the type of the ModTile of this mod with the given name. Returns 0 if no ModTile with the given name is found.
		/// </summary>
		/// <param name="name">The name.</param>
		/// <returns></returns>
		public int TileType(string name) => GetTile(name)?.Type ?? 0;

		/// <summary>
		/// Same as the other TileType, but assumes that the class name and internal name are the same.
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <returns></returns>
		public int TileType<T>() where T : ModTile => TileType(typeof(T).Name);

		/// <summary>
		/// Adds the given GlobalTile instance to this mod with the provided name.
		/// </summary>
		/// <param name="name">The name.</param>
		/// <param name="globalTile">The global tile.</param>
		public void AddGlobalTile(string name, GlobalTile globalTile)
		{
			if (!loading)
				throw new Exception("AddGlobalTile can only be called from Mod.Load or Mod.Autoload");

			globalTile.mod = this;
			globalTile.Name = name;

			globalTiles[name] = globalTile;
			TileLoader.globalTiles.Add(globalTile);
		}

		/// <summary>
		/// Gets the GlobalTile instance with the given name from this mod.
		/// </summary>
		/// <param name="name">The name.</param>
		/// <returns></returns>
		public GlobalTile GetGlobalTile(string name) => globalTiles.TryGetValue(name, out var globalTile) ? globalTile : null;

		/// <summary>
		/// Same as the other GetGlobalTile, but assumes that the class name and internal name are the same.
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <returns></returns>
		public T GetGlobalTile<T>() where T : GlobalTile => (T)GetGlobalTile(typeof(T).Name);

		/// <summary>
		/// Manually add a tile entity during Load.
		/// </summary>
		public void AddTileEntity(string name, ModTileEntity entity)
		{
			if (!loading)
				throw new Exception("AddTileEntity can only be called from Mod.Load or Mod.Autoload");

			int id = ModTileEntity.ReserveTileEntityID();
			entity.mod = this;
			entity.Name = name;
			entity.Type = id;
			entity.type = (byte)id;

			tileEntities[name] = entity;
			ModTileEntity.tileEntities.Add(entity);
		}

		/// <summary>
		/// Gets the ModTileEntity of this mod corresponding to the given name. Returns null if no ModTileEntity with the given name is found.
		/// </summary>
		/// <param name="name">The name.</param>
		/// <returns></returns>
		public ModTileEntity GetTileEntity(string name) => 
			tileEntities.TryGetValue(name, out var tileEntity) ? tileEntity : null;

		/// <summary>
		/// Same as the other GetTileEntity, but assumes that the class name and internal name are the same.
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <returns></returns>
		public T GetTileEntity<T>() where T : ModTileEntity => (T)GetTileEntity(typeof(T).Name);

		/// <summary>
		/// Gets the type of the ModTileEntity of this mod with the given name. Returns -1 if no ModTileEntity with the given name is found.
		/// </summary>
		/// <param name="name">The name.</param>
		/// <returns></returns>
		public int TileEntityType(string name) => GetTileEntity(name)?.Type ?? -1;

		/// <summary>
		/// Same as the other TileEntityType, but assumes that the class name and internal name are the same.
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <returns></returns>
		public int TileEntityType<T>() where T : ModTileEntity => TileEntityType(typeof(T).Name);

		/// <summary>
		/// Adds a type of wall to the game with the specified name and texture.
		/// </summary>
		/// <param name="name">The name.</param>
		/// <param name="wall">The wall.</param>
		/// <param name="texture">The texture.</param>
		public void AddWall(string name, ModWall wall, string texture)
		{
			if (!loading)
				throw new Exception("AddWall can only be called from Mod.Load or Mod.Autoload");

			wall.mod = this;
			wall.Name = name;
			wall.Type = (ushort)WallLoader.ReserveWallID();
			wall.texture = texture;

			walls[name] = wall;
			WallLoader.walls.Add(wall);
		}

		/// <summary>
		/// Gets the ModWall of this mod corresponding to the given name. Returns null if no ModWall with the given name is found.
		/// </summary>
		/// <param name="name">The name.</param>
		/// <returns></returns>
		public ModWall GetWall(string name) => walls.TryGetValue(name, out var wall) ? wall : null;

		public T GetWall<T>() where T : ModWall => (T)GetWall(typeof(T).Name);

		/// <summary>
		/// Gets the type of the ModWall of this mod with the given name. Returns 0 if no ModWall with the given name is found.
		/// </summary>
		/// <param name="name">The name.</param>
		/// <returns></returns>
		public int WallType(string name) => GetWall(name)?.Type ?? 0;

		/// <summary>
		/// Same as the other WallType, but assumes that the class name and internal name are the same.
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <returns></returns>
		public int WallType<T>() where T : ModWall => WallType(typeof(T).Name);

		/// <summary>
		/// Adds the given GlobalWall instance to this mod with the provided name.
		/// </summary>
		/// <param name="name">The name.</param>
		/// <param name="globalWall">The global wall.</param>
		public void AddGlobalWall(string name, GlobalWall globalWall)
		{
			if (!loading)
				throw new Exception("AddGlobalWall can only be called from Mod.Load or Mod.Autoload");

			globalWall.mod = this;
			globalWall.Name = name;

			globalWalls[name] = globalWall;
			WallLoader.globalWalls.Add(globalWall);
		}

		/// <summary>
		/// Gets the GlobalWall instance with the given name from this mod.
		/// </summary>
		/// <param name="name">The name.</param>
		/// <returns></returns>
		public GlobalWall GetGlobalWall(string name) => globalWalls.TryGetValue(name, out var globalWall) ? globalWall : null;

		public T GetGlobalWall<T>() where T : GlobalWall => (T)GetGlobalWall(typeof(T).Name);

		/// <summary>
		/// Adds a type of projectile to the game with the specified name.
		/// </summary>
		/// <param name="name">The name.</param>
		/// <param name="projectile">The projectile.</param>
		public void AddProjectile(string name, ModProjectile projectile)
		{
			if (!loading)
				throw new Exception("AddProjectile can only be called from Mod.Load or Mod.Autoload");

			if (projectiles.ContainsKey(name))
				throw new Exception("You tried to add 2 ModProjectile with the same name: " + name + ". Maybe 2 classes share a classname but in different namespaces while autoloading or you manually called AddProjectile with 2 projectiles of the same name.");

			projectile.mod = this;
			projectile.Name = name;
			projectile.projectile.type = ProjectileLoader.ReserveProjectileID();
			projectile.DisplayName = GetOrCreateTranslation(string.Format("Mods.{0}.ProjectileName.{1}", Name, name));

			projectiles[name] = projectile;
			ProjectileLoader.projectiles.Add(projectile);
		}

		/// <summary>
		/// Gets the ModProjectile of this mod corresponding to the given name. Returns null if no ModProjectile with the given name is found.
		/// </summary>
		/// <param name="name">The name.</param>
		/// <returns></returns>
		public ModProjectile GetProjectile(string name) => projectiles.TryGetValue(name, out var proj) ? proj : null;

		public T GetProjectile<T>() where T : ModProjectile => (T)GetProjectile(typeof(T).Name);

		/// <summary>
		/// Gets the type of the ModProjectile of this mod with the given name. Returns 0 if no ModProjectile with the given name is found.
		/// </summary>
		/// <param name="name">The name.</param>
		/// <returns></returns>
		public int ProjectileType(string name) => GetProjectile(name)?.projectile.type ?? 0;

		/// <summary>
		/// Same as the other ProjectileType, but assumes that the class name and internal name are the same.
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <returns></returns>
		public int ProjectileType<T>() where T : ModProjectile => ProjectileType(typeof(T).Name);

		/// <summary>
		/// Adds the given GlobalProjectile instance to this mod with the provided name.
		/// </summary>
		/// <param name="name">The name.</param>
		/// <param name="globalProjectile">The global projectile.</param>
		public void AddGlobalProjectile(string name, GlobalProjectile globalProjectile)
		{
			if (!loading)
				throw new Exception("AddGlobalProjectile can only be called from Mod.Load or Mod.Autoload");

			ProjectileLoader.VerifyGlobalProjectile(globalProjectile);

			globalProjectile.mod = this;
			globalProjectile.Name = name;

			globalProjectiles[name] = globalProjectile;
			globalProjectile.index = ProjectileLoader.globalProjectiles.Count;
			ProjectileLoader.globalIndexes[Name + ':' + name] = ProjectileLoader.globalProjectiles.Count;
			if (ProjectileLoader.globalIndexesByType.ContainsKey(globalProjectile.GetType()))
			{
				ProjectileLoader.globalIndexesByType[globalProjectile.GetType()] = -1;
			}
			else
			{
				ProjectileLoader.globalIndexesByType[globalProjectile.GetType()] = ProjectileLoader.globalProjectiles.Count;
			}
			ProjectileLoader.globalProjectiles.Add(globalProjectile);
		}

		/// <summary>
		/// Gets the GlobalProjectile instance with the given name from this mod.
		/// </summary>
		/// <param name="name">The name.</param>
		/// <returns></returns>
		public GlobalProjectile GetGlobalProjectile(string name) => globalProjectiles.TryGetValue(name, out var globalProj) ? globalProj : null;

		public T GetGlobalProjectile<T>() where T : GlobalProjectile => (T)GetGlobalProjectile(typeof(T).Name);

		/// <summary>
		/// Adds a type of NPC to the game with the specified name and texture. Also allows you to give the NPC alternate textures.
		/// </summary>
		/// <param name="name">The name.</param>
		/// <param name="npc">The NPC.</param>
		public void AddNPC(string name, ModNPC npc)
		{
			if (!loading)
				throw new Exception("AddNPC can only be called from Mod.Load or Mod.Autoload");

			if (npcs.ContainsKey(name))
				throw new Exception("You tried to add 2 ModNPC with the same name: " + name + ". Maybe 2 classes share a classname but in different namespaces while autoloading or you manually called AddNPC with 2 npcs of the same name.");

			npc.mod = this;
			npc.Name = name;
			npc.npc.type = NPCLoader.ReserveNPCID();
			npc.DisplayName = GetOrCreateTranslation(string.Format("Mods.{0}.NPCName.{1}", Name, name));

			npcs[name] = npc;
			NPCLoader.npcs.Add(npc);
		}

		/// <summary>
		/// Gets the ModNPC of this mod corresponding to the given name. Returns null if no ModNPC with the given name is found.
		/// </summary>
		/// <param name="name">The name.</param>
		/// <returns></returns>
		public ModNPC GetNPC(string name) => npcs.TryGetValue(name, out var npc) ? npc : null;

		public T GetNPC<T>() where T : ModNPC => (T)GetNPC(typeof(T).Name);

		/// <summary>
		/// Gets the type of the ModNPC of this mod with the given name. Returns 0 if no ModNPC with the given name is found.
		/// </summary>
		/// <param name="name">The name.</param>
		/// <returns></returns>
		public int NPCType(string name) => GetNPC(name)?.npc.type ?? 0;

		/// <summary>
		/// Same as the other NPCType, but assumes that the class name and internal name are the same.
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <returns></returns>
		public int NPCType<T>() where T : ModNPC => NPCType(typeof(T).Name);

		/// <summary>
		/// Adds the given GlobalNPC instance to this mod with the provided name.
		/// </summary>
		/// <param name="name">The name.</param>
		/// <param name="globalNPC">The global NPC.</param>
		public void AddGlobalNPC(string name, GlobalNPC globalNPC)
		{
			if (!loading)
				throw new Exception("AddGlobalNPC can only be called from Mod.Load or Mod.Autoload");

			NPCLoader.VerifyGlobalNPC(globalNPC);

			globalNPC.mod = this;
			globalNPC.Name = name;

			globalNPCs[name] = globalNPC;
			globalNPC.index = NPCLoader.globalNPCs.Count;
			NPCLoader.globalIndexes[Name + ':' + name] = NPCLoader.globalNPCs.Count;
			if (NPCLoader.globalIndexesByType.ContainsKey(globalNPC.GetType()))
			{
				NPCLoader.globalIndexesByType[globalNPC.GetType()] = -1;
			}
			else
			{
				NPCLoader.globalIndexesByType[globalNPC.GetType()] = NPCLoader.globalNPCs.Count;
			}
			NPCLoader.globalNPCs.Add(globalNPC);
		}

		/// <summary>
		/// Gets the GlobalNPC instance with the given name from this mod.
		/// </summary>
		/// <param name="name">The name.</param>
		/// <returns></returns>
		public GlobalNPC GetGlobalNPC(string name) => globalNPCs.TryGetValue(name, out var globalNPC) ? globalNPC : null;

		public T GetGlobalNPC<T>() where T : GlobalNPC => (T)GetGlobalNPC(typeof(T).Name);

		/// <summary>
		/// Assigns a head texture to the given town NPC type.
		/// </summary>
		/// <param name="npcType">Type of the NPC.</param>
		/// <param name="texture">The texture.</param>
		/// <exception cref="MissingResourceException"></exception>
		public void AddNPCHeadTexture(int npcType, string texture)
		{
			if (!loading)
				throw new Exception("AddNPCHeadTexture can only be called from Mod.Load or Mod.Autoload");

			int slot = NPCHeadLoader.ReserveHeadSlot();
			NPCHeadLoader.heads[texture] = slot;
			if (!Main.dedServ)
			{
				ModContent.GetTexture(texture);
			}
			/*else if (Main.dedServ && !(ModLoader.FileExists(texture + ".png") || ModLoader.FileExists(texture + ".rawimg")))
			{
				throw new MissingResourceException(texture);
			}*/
			NPCHeadLoader.npcToHead[npcType] = slot;
			NPCHeadLoader.headToNPC[slot] = npcType;
		}

		/// <summary>
		/// Assigns a head texture that can be used by NPCs on the map.
		/// </summary>
		/// <param name="texture">The texture.</param>
		/// <param name="npcType">An optional npc id for NPCID.Sets.BossHeadTextures</param>
		public void AddBossHeadTexture(string texture, int npcType = -1)
		{
			if (!loading)
				throw new Exception("AddBossHeadTexture can only be called from Mod.Load or Mod.Autoload");

			int slot = NPCHeadLoader.ReserveBossHeadSlot(texture);
			NPCHeadLoader.bossHeads[texture] = slot;
			ModContent.GetTexture(texture);
			if (npcType >= 0)
			{
				NPCHeadLoader.npcToBossHead[npcType] = slot;
			}
		}

		/// <summary>
		/// Adds a type of ModPlayer to this mod. All ModPlayer types will be newly created and attached to each player that is loaded.
		/// </summary>
		/// <param name="name">The name.</param>
		/// <param name="player">The player.</param>
		public void AddPlayer(string name, ModPlayer player)
		{
			if (!loading)
				throw new Exception("AddPlayer can only be called from Mod.Load or Mod.Autoload");

			player.mod = this;
			player.Name = name;

			players[name] = player;
			PlayerHooks.Add(player);
		}

		/// <summary>
		/// Gets the ModPlayer of this mod corresponding to the given name. Returns null if no ModPlayer with the given name is found.
		/// </summary>
		/// <param name="name">The name.</param>
		/// <returns></returns>
		public ModPlayer GetPlayer(string name) => players.TryGetValue(name, out var player) ? player : null;

		public T GetPlayer<T>() where T : ModPlayer => (T)GetPlayer(typeof(T).Name);

		/// <summary>
		/// Adds a type of buff to the game with the specified internal name and texture.
		/// </summary>
		/// <param name="name">The name.</param>
		/// <param name="buff">The buff.</param>
		/// <param name="texture">The texture.</param>
		public void AddBuff(string name, ModBuff buff, string texture)
		{
			if (!loading)
				throw new Exception("AddBuff can only be called from Mod.Load or Mod.Autoload");

			if (buffs.ContainsKey(name))
				throw new Exception("You tried to add 2 ModBuff with the same name: " + name + ". Maybe 2 classes share a classname but in different namespaces while autoloading or you manually called AddBuff with 2 buffs of the same name.");

			buff.mod = this;
			buff.Name = name;
			buff.Type = BuffLoader.ReserveBuffID();
			buff.texture = texture;
			buff.DisplayName = GetOrCreateTranslation(string.Format("Mods.{0}.BuffName.{1}", Name, name));
			buff.Description = GetOrCreateTranslation(string.Format("Mods.{0}.BuffDescription.{1}", Name, name));

			buffs[name] = buff;
			BuffLoader.buffs.Add(buff);
		}

		/// <summary>
		/// Gets the ModBuff of this mod corresponding to the given name. Returns null if no ModBuff with the given name is found.
		/// </summary>
		/// <param name="name">The name.</param>
		/// <returns></returns>
		public ModBuff GetBuff(string name) => buffs.TryGetValue(name, out var buff) ? buff : null;

		public T GetBuff<T>() where T : ModBuff => (T)GetBuff(typeof(T).Name);

		/// <summary>
		/// Gets the type of the ModBuff of this mod corresponding to the given name. Returns 0 if no ModBuff with the given name is found.
		/// </summary>
		/// <param name="name">The name.</param>
		/// <returns></returns>
		public int BuffType(string name) => GetBuff(name)?.Type ?? 0;

		/// <summary>
		/// Same as the other BuffType, but assumes that the class name and internal name are the same.
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <returns></returns>
		public int BuffType<T>() where T : ModBuff => BuffType(typeof(T).Name);

		/// <summary>
		/// Adds the given GlobalBuff instance to this mod using the provided name.
		/// </summary>
		/// <param name="name">The name.</param>
		/// <param name="globalBuff">The global buff.</param>
		public void AddGlobalBuff(string name, GlobalBuff globalBuff)
		{
			globalBuff.mod = this;
			globalBuff.Name = name;

			globalBuffs[name] = globalBuff;
			BuffLoader.globalBuffs.Add(globalBuff);
		}

		/// <summary>
		/// Gets the GlobalBuff with the given name from this mod.
		/// </summary>
		/// <param name="name">The name.</param>
		/// <returns></returns>
		public GlobalBuff GetGlobalBuff(string name) => globalBuffs.TryGetValue(name, out var globalBuff) ? globalBuff : null;

		public T GetGlobalBuff<T>() where T : GlobalBuff => (T)GetGlobalBuff(typeof(T).Name);

		/// <summary>
		/// Adds the given mount to the game with the given name and texture. The extraTextures dictionary should optionally map types of mount textures to the texture paths you want to include.
		/// </summary>
		/// <param name="name">The name.</param>
		/// <param name="mount">The mount.</param>
		/// <param name="texture">The texture.</param>
		/// <param name="extraTextures">The extra textures.</param>
		public void AddMount(string name, ModMountData mount, string texture,
			IDictionary<MountTextureType, string> extraTextures = null)
		{
			if (!loading)
				throw new Exception("AddMount can only be called from Mod.Load or Mod.Autoload");

			if (Mount.mounts == null || Mount.mounts.Length == MountID.Count)
				Mount.Initialize();

			mount.mod = this;
			mount.Name = name;
			mount.Type = MountLoader.ReserveMountID();
			mount.texture = texture;

			mountDatas[name] = mount;
			MountLoader.mountDatas[mount.Type] = mount;

			if (extraTextures == null)
				return;

			foreach (var entry in extraTextures)
			{
				if (!ModContent.TextureExists(entry.Value))
					continue;

				Texture2D extraTexture = ModContent.GetTexture(entry.Value);
				switch (entry.Key)
				{
					case MountTextureType.Back:
						mount.mountData.backTexture = extraTexture;
						break;
					case MountTextureType.BackGlow:
						mount.mountData.backTextureGlow = extraTexture;
						break;
					case MountTextureType.BackExtra:
						mount.mountData.backTextureExtra = extraTexture;
						break;
					case MountTextureType.BackExtraGlow:
						mount.mountData.backTextureExtraGlow = extraTexture;
						break;
					case MountTextureType.Front:
						mount.mountData.frontTexture = extraTexture;
						break;
					case MountTextureType.FrontGlow:
						mount.mountData.frontTextureGlow = extraTexture;
						break;
					case MountTextureType.FrontExtra:
						mount.mountData.frontTextureExtra = extraTexture;
						break;
					case MountTextureType.FrontExtraGlow:
						mount.mountData.frontTextureExtraGlow = extraTexture;
						break;
				}
			}
		}

		/// <summary>
		/// Gets the ModMountData instance of this mod corresponding to the given name. Returns null if no ModMountData has the given name.
		/// </summary>
		/// <param name="name">The name.</param>
		/// <returns></returns>
		public ModMountData GetMount(string name) => mountDatas.TryGetValue(name, out var modMountData) ? modMountData : null;

		public T GetMount<T>() where T : ModMountData => (T)GetMount(typeof(T).Name);

		/// <summary>
		/// Gets the ID of the ModMountData instance corresponding to the given name. Returns 0 if no ModMountData has the given name.
		/// </summary>
		/// <param name="name">The name.</param>
		/// <returns></returns>
		public int MountType(string name) => GetMount(name)?.Type ?? 0;

		/// <summary>
		/// Same as the other MountType, but assumes that the class name and internal name are the same.
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <returns></returns>
		public int MountType<T>() where T : ModMountData => MountType(typeof(T).Name);

		/// <summary>
		/// Adds a ModWorld to this mod with the given name.
		/// </summary>
		/// <param name="name">The name.</param>
		/// <param name="modWorld">The mod world.</param>
		public void AddModWorld(string name, ModWorld modWorld)
		{
			if (!loading)
				throw new Exception("AddModWorld can only be called from Mod.Load or Mod.Autoload");

			modWorld.mod = this;
			modWorld.Name = name;

			worlds[name] = modWorld;
			WorldHooks.Add(modWorld);
		}

		/// <summary>
		/// Gets the ModWorld instance with the given name from this mod.
		/// </summary>
		/// <param name="name">The name.</param>
		/// <returns></returns>
		public ModWorld GetModWorld(string name) => worlds.TryGetValue(name, out var world) ? world : null;

		/// <summary>
		/// Same as the other GetModWorld, but assumes that the class name and internal name are the same.
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <returns></returns>
		public T GetModWorld<T>() where T : ModWorld => (T)GetModWorld(typeof(T).Name);

		/// <summary>
		/// Adds the given underground background style with the given name to this mod.
		/// </summary>
		/// <param name="name">The name.</param>
		/// <param name="ugBgStyle">The ug bg style.</param>
		public void AddUgBgStyle(string name, ModUgBgStyle ugBgStyle)
		{
			if (!loading)
				throw new Exception("AddUgBgStyle can only be called from Mod.Load or Mod.Autoload");

			ugBgStyle.mod = this;
			ugBgStyle.Name = name;
			ugBgStyle.Slot = UgBgStyleLoader.ReserveBackgroundSlot();

			ugBgStyles[name] = ugBgStyle;
			UgBgStyleLoader.ugBgStyles.Add(ugBgStyle);
		}

		/// <summary>
		/// Returns the underground background style corresponding to the given name.
		/// </summary>
		/// <param name="name">The name.</param>
		/// <returns></returns>
		public ModUgBgStyle GetUgBgStyle(string name) => ugBgStyles.TryGetValue(name, out var bgStyle) ? bgStyle : null;

		public T GetUgBgStyle<T>() where T : ModUgBgStyle => (T)GetUgBgStyle(typeof(T).Name);

		/// <summary>
		/// Adds the given surface background style with the given name to this mod.
		/// </summary>
		/// <param name="name">The name.</param>
		/// <param name="surfaceBgStyle">The surface bg style.</param>
		public void AddSurfaceBgStyle(string name, ModSurfaceBgStyle surfaceBgStyle)
		{
			if (!loading)
				throw new Exception("AddSurfaceBgStyle can only be called from Mod.Load or Mod.Autoload");

			surfaceBgStyle.mod = this;
			surfaceBgStyle.Name = name;
			surfaceBgStyle.Slot = SurfaceBgStyleLoader.ReserveBackgroundSlot();

			surfaceBgStyles[name] = surfaceBgStyle;
			SurfaceBgStyleLoader.surfaceBgStyles.Add(surfaceBgStyle);
		}

		/// <summary>
		/// Returns the surface background style corresponding to the given name.
		/// </summary>
		/// <param name="name">The name.</param>
		/// <returns></returns>
		public ModSurfaceBgStyle GetSurfaceBgStyle(string name) => surfaceBgStyles.TryGetValue(name, out var bgStyle) ? bgStyle : null;

		public T GetSurfaceBgStyle<T>() where T : ModSurfaceBgStyle => (T)GetSurfaceBgStyle(typeof(T).Name);

		/// <summary>
		/// Returns the Slot of the surface background style corresponding to the given name.
		/// </summary>
		/// <param name="name">The name.</param>
		/// <returns></returns>
		public int GetSurfaceBgStyleSlot(string name) => GetSurfaceBgStyle(name)?.Slot ?? -1;

		public int GetSurfaceBgStyleSlot<T>() where T : ModSurfaceBgStyle => GetSurfaceBgStyleSlot(typeof(T).Name);

		/// <summary>
		/// Adds the given global background style with the given name to this mod.
		/// </summary>
		/// <param name="name">The name.</param>
		/// <param name="globalBgStyle">The global bg style.</param>
		public void AddGlobalBgStyle(string name, GlobalBgStyle globalBgStyle)
		{
			if (!loading)
				throw new Exception("AddGlobalBgStyle can only be called from Mod.Load or Mod.Autoload");

			globalBgStyle.mod = this;
			globalBgStyle.Name = name;

			globalBgStyles[name] = globalBgStyle;
			GlobalBgStyleLoader.globalBgStyles.Add(globalBgStyle);
		}

		/// <summary>
		/// Returns the global background style corresponding to the given name.
		/// </summary>
		/// <param name="name">The name.</param>
		/// <returns></returns>
		public GlobalBgStyle GetGlobalBgStyle(string name) => globalBgStyles.TryGetValue(name, out var bgStyle) ? bgStyle : null;

		public T GetGlobalBgStyle<T>() where T : GlobalBgStyle => (T)GetGlobalBgStyle(typeof(T).Name);

		/// <summary>
		/// Adds the given water style to the game with the given name, texture path, and block texture path.
		/// </summary>
		/// <param name="name">The name.</param>
		/// <param name="waterStyle">The water style.</param>
		/// <param name="texture">The texture.</param>
		/// <param name="blockTexture">The block texture.</param>
		public void AddWaterStyle(string name, ModWaterStyle waterStyle, string texture, string blockTexture)
		{
			if (!loading)
				throw new Exception("AddWaterStyle can only be called from Mod.Load or Mod.Autoload");

			waterStyle.mod = this;
			waterStyle.Name = name;
			waterStyle.Type = WaterStyleLoader.ReserveStyle();
			waterStyle.texture = texture;
			waterStyle.blockTexture = blockTexture;

			waterStyles[name] = waterStyle;
			WaterStyleLoader.waterStyles.Add(waterStyle);
		}

		/// <summary>
		/// Returns the water style with the given name from this mod.
		/// </summary>
		/// <param name="name">The name.</param>
		/// <returns></returns>
		public ModWaterStyle GetWaterStyle(string name) => waterStyles.TryGetValue(name, out var waterStyle) ? waterStyle : null;

		public T GetWaterStyle<T>() where T : ModWaterStyle => (T)GetWaterStyle(typeof(T).Name);

		/// <summary>
		/// Adds the given waterfall style to the game with the given name and texture path.
		/// </summary>
		/// <param name="name">The name.</param>
		/// <param name="waterfallStyle">The waterfall style.</param>
		/// <param name="texture">The texture.</param>
		public void AddWaterfallStyle(string name, ModWaterfallStyle waterfallStyle, string texture)
		{
			if (!loading)
				throw new Exception("AddWaterfallStyle can only be called from Mod.Load or Mod.Autoload");

			waterfallStyle.mod = this;
			waterfallStyle.Name = name;
			waterfallStyle.Type = WaterfallStyleLoader.ReserveStyle();
			waterfallStyle.texture = texture;

			waterfallStyles[name] = waterfallStyle;
			WaterfallStyleLoader.waterfallStyles.Add(waterfallStyle);
		}

		/// <summary>
		/// Returns the waterfall style with the given name from this mod.
		/// </summary>
		/// <param name="name">The name.</param>
		/// <returns></returns>
		public ModWaterfallStyle GetWaterfallStyle(string name) => waterfallStyles.TryGetValue(name, out var waterfallStyle) ? waterfallStyle : null;

		public T GetWaterfallStyle<T>() where T : ModWaterfallStyle => (T)GetWaterfallStyle(typeof(T).Name);

		/// <summary>
		/// Returns the waterfall style corresponding to the given name.
		/// </summary>
		/// <param name="name">The name.</param>
		/// <returns></returns>
		public int GetWaterfallStyleSlot(string name) => GetWaterfallStyle(name)?.Type ?? -1;

		public int GetWaterfallStyleSlot<T>() where T : ModWaterfallStyle => GetWaterfallStyleSlot(typeof(T).Name);

		/// <summary>
		/// Adds the given texture to the game as a custom gore, with the given custom gore behavior. If no custom gore behavior is provided, the custom gore will have the default vanilla behavior.
		/// </summary>
		/// <param name="texture">The texture.</param>
		/// <param name="modGore">The mod gore.</param>
		public void AddGore(string texture, ModGore modGore = null)
		{
			if (!loading)
				throw new Exception("AddGore can only be called from Mod.Load or Mod.Autoload");

			int id = ModGore.ReserveGoreID();
			ModGore.gores[texture] = id;
			if (modGore != null)
			{
				ModGore.modGores[id] = modGore;
			}
		}

		/// <summary>
		/// Shorthand for calling ModGore.GetGoreSlot(this.Name + '/' + name).
		/// </summary>
		/// <param name="name">The name.</param>
		/// <returns></returns>
		public int GetGoreSlot(string name) => ModGore.GetGoreSlot(Name + '/' + name);

		/// <summary>
		/// Same as the other GetGoreSlot, but assumes that the class name and internal name are the same.
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <returns></returns>
		public int GetGoreSlot<T>() where T : ModGore => GetGoreSlot(typeof(T).Name);

		/// <summary>
		/// Adds the given sound file to the game as the given type of sound and with the given custom sound playing. If no ModSound instance is provided, the custom sound will play in a similar manner as the default vanilla ones.
		/// </summary>
		/// <param name="type">The type.</param>
		/// <param name="soundPath">The sound path.</param>
		/// <param name="modSound">The mod sound.</param>
		public void AddSound(SoundType type, string soundPath, ModSound modSound = null)
		{
			if (!loading)
				throw new Exception("AddSound can only be called from Mod.Load or Mod.Autoload");
			int id = SoundLoader.ReserveSoundID(type);
			SoundLoader.sounds[type][soundPath] = id;
			if (modSound != null)
			{
				SoundLoader.modSounds[type][id] = modSound;
				modSound.sound = ModContent.GetSound(soundPath);
			}
		}

		/// <summary>
		/// Shorthand for calling SoundLoader.GetSoundSlot(type, this.Name + '/' + name).
		/// </summary>
		/// <param name="type">The type.</param>
		/// <param name="name">The name.</param>
		/// <returns></returns>
		public int GetSoundSlot(SoundType type, string name) => SoundLoader.GetSoundSlot(type, Name + '/' + name);

		/// <summary>
		/// Shorthand for calling SoundLoader.GetLegacySoundSlot(type, this.Name + '/' + name).
		/// </summary>
		/// <param name="type">The type.</param>
		/// <param name="name">The name.</param>
		/// <returns></returns>
		public LegacySoundStyle GetLegacySoundSlot(SoundType type, string name) => SoundLoader.GetLegacySoundSlot(type, Name + '/' + name);

		/// <summary>
		/// Adds a texture to the list of background textures and assigns it a background texture slot.
		/// </summary>
		/// <param name="texture">The texture.</param>
		public void AddBackgroundTexture(string texture)
		{
			if (!loading)
				throw new Exception("AddBackgroundTexture can only be called from Mod.Load or Mod.Autoload");

			BackgroundTextureLoader.backgrounds[texture] = BackgroundTextureLoader.ReserveBackgroundSlot();
			ModContent.GetTexture(texture);
		}

		/// <summary>
		/// Gets the texture slot corresponding to the specified texture name. Shorthand for calling BackgroundTextureLoader.GetBackgroundSlot(this.Name + '/' + name).
		/// </summary>
		/// <param name="name">The name.</param>
		/// <returns></returns>
		public int GetBackgroundSlot(string name) => BackgroundTextureLoader.GetBackgroundSlot(Name + '/' + name);

		/// <summary>
		/// Manually add a Global Recipe during Load
		/// </summary>
		/// <param name="name">The name.</param>
		/// <param name="globalRecipe">The global recipe.</param>
		public void AddGlobalRecipe(string name, GlobalRecipe globalRecipe)
		{
			if (!loading)
				throw new Exception("AddGlobalRecipe can only be called from Mod.Load or Mod.Autoload");

			globalRecipe.mod = this;
			globalRecipe.Name = name;

			globalRecipes[name] = globalRecipe;
			RecipeHooks.Add(globalRecipe);
		}

		/// <summary>
		/// Gets the global recipe corresponding to the specified name.
		/// </summary>
		/// <param name="name">The name.</param>
		/// <returns></returns>
		public GlobalRecipe GetGlobalRecipe(string name) => globalRecipes.TryGetValue(name, out var globalRecipe) ? globalRecipe : null;

		public T GetGlobalRecipe<T>() where T : GlobalRecipe => (T)GetGlobalRecipe(typeof(T).Name);

		/// <summary>
		/// Manually add a Command during Load
		/// </summary>
		public void AddCommand(string name, ModCommand mc)
		{
			if (!loading)
				throw new Exception("AddCommand can only be called from Mod.Load or Mod.Autoload");

			mc.mod = this;
			mc.Name = name;
			CommandManager.Add(mc);
		}

		/// <summary>
		/// Allows you to tie a music ID, and item ID, and a tile ID together to form a music box. When music with the given ID is playing, equipped music boxes have a chance to change their ID to the given item type. When an item with the given item type is equipped, it will play the music that has musicSlot as its ID. When a tile with the given type and Y-frame is nearby, if its X-frame is >= 36, it will play the music that has musicSlot as its ID.
		/// </summary>
		/// <param name="musicSlot">The music slot.</param>
		/// <param name="itemType">Type of the item.</param>
		/// <param name="tileType">Type of the tile.</param>
		/// <param name="tileFrameY">The tile frame y.</param>
		/// <exception cref="System.ArgumentOutOfRangeException">
		/// Cannot assign music box to vanilla music ID " + musicSlot
		/// or
		/// Music ID " + musicSlot + " does not exist
		/// or
		/// Cannot assign music box to vanilla item ID " + itemType
		/// or
		/// Item ID " + itemType + " does not exist
		/// or
		/// Cannot assign music box to vanilla tile ID " + tileType
		/// or
		/// Tile ID " + tileType + " does not exist
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// Music ID " + musicSlot + " has already been assigned a music box
		/// or
		/// Item ID " + itemType + " has already been assigned a music
		/// or
		/// or
		/// Y-frame must be divisible by 36
		/// </exception>
		public void AddMusicBox(int musicSlot, int itemType, int tileType, int tileFrameY = 0)
		{
			if (!loading)
				throw new Exception("AddMusicBox can only be called from Mod.Load or Mod.Autoload");

			if (musicSlot < Main.maxMusic)
			{
				throw new ArgumentOutOfRangeException("Cannot assign music box to vanilla music ID " + musicSlot);
			}
			if (musicSlot >= SoundLoader.SoundCount(SoundType.Music))
			{
				throw new ArgumentOutOfRangeException("Music ID " + musicSlot + " does not exist");
			}
			if (itemType < ItemID.Count)
			{
				throw new ArgumentOutOfRangeException("Cannot assign music box to vanilla item ID " + itemType);
			}
			if (ItemLoader.GetItem(itemType) == null)
			{
				throw new ArgumentOutOfRangeException("Item ID " + itemType + " does not exist");
			}
			if (tileType < TileID.Count)
			{
				throw new ArgumentOutOfRangeException("Cannot assign music box to vanilla tile ID " + tileType);
			}
			if (TileLoader.GetTile(tileType) == null)
			{
				throw new ArgumentOutOfRangeException("Tile ID " + tileType + " does not exist");
			}
			if (SoundLoader.musicToItem.ContainsKey(musicSlot))
			{
				throw new ArgumentException("Music ID " + musicSlot + " has already been assigned a music box");
			}
			if (SoundLoader.itemToMusic.ContainsKey(itemType))
			{
				throw new ArgumentException("Item ID " + itemType + " has already been assigned a music");
			}
			if (!SoundLoader.tileToMusic.ContainsKey(tileType))
			{
				SoundLoader.tileToMusic[tileType] = new Dictionary<int, int>();
			}
			if (SoundLoader.tileToMusic[tileType].ContainsKey(tileFrameY))
			{
				string message = "Y-frame " + tileFrameY + " of tile type " + tileType + " has already been assigned a music";
				throw new ArgumentException(message);
			}
			if (tileFrameY % 36 != 0)
			{
				throw new ArgumentException("Y-frame must be divisible by 36");
			}
			SoundLoader.musicToItem[musicSlot] = itemType;
			SoundLoader.itemToMusic[itemType] = musicSlot;
			SoundLoader.tileToMusic[tileType][tileFrameY] = musicSlot;
		}

		/// <summary>
		/// Registers a hotkey with a name and defaultKey. Use the returned ModHotKey to detect when buttons are pressed. Do this in a ModPlayer.ProcessTriggers.
		/// </summary>
		/// <param name="name">The name.</param>
		/// <param name="defaultKey">The default key.</param>
		/// <returns></returns>
		public ModHotKey RegisterHotKey(string name, string defaultKey)
		{
			if (!loading)
				throw new Exception("RegisterHotKey can only be called from Mod.Load or Mod.Autoload");

			return ModContent.RegisterHotKey(this, name, defaultKey);
		}

		/// <summary>
		/// Creates a ModTranslation object that you can use in AddTranslation.
		/// </summary>
		/// <param name="key">The key for the ModTranslation. The full key will be Mods.ModName.key</param>
		public ModTranslation CreateTranslation(string key) => 
			new ModTranslation(string.Format("Mods.{0}.{1}", Name, key));

		/// <summary>
		/// Adds a ModTranslation to the game so that you can use Language.GetText to get a LocalizedText.
		/// </summary>
		public void AddTranslation(ModTranslation translation)
		{
			translations[translation.Key] = translation;
		}

		internal ModTranslation GetOrCreateTranslation(string key, bool defaultEmpty = false)
		{
			key = key.Replace(" ", "_");
			return translations.TryGetValue(key, out var translation) ? translation : new ModTranslation(key, defaultEmpty);
		}

		/// <summary>
		/// Shorthand for calling ModLoader.GetFileBytes(this.FileName(name)). Note that file extensions are used here.
		/// </summary>
		/// <param name="name">The name.</param>
		/// <returns></returns>
		public byte[] GetFileBytes(string name) => File?.GetFile(name);

		/// <summary>
		/// Shorthand for calling ModLoader.FileExists(this.FileName(name)). Note that file extensions are used here.
		/// </summary>
		/// <param name="name">The name.</param>
		/// <returns></returns>
		public bool FileExists(string name) => File != null && File.HasFile(name);

		/// <summary>
		/// Shorthand for calling ModContent.GetTexture(this.FileName(name)).
		/// </summary>
		/// <exception cref="MissingResourceException"></exception>
		public Texture2D GetTexture(string name)
		{
			if (!textures.TryGetValue(name, out var t))
				throw new MissingResourceException(name, textures.Keys);

			return t;
		}

		/// <summary>
		/// Shorthand for calling ModLoader.TextureExists(this.FileName(name)).
		/// </summary>
		/// <param name="name">The name.</param>
		/// <returns></returns>
		public bool TextureExists(string name) => textures.ContainsKey(name);

		/// <summary>
		/// Shorthand for calling ModLoader.AddTexture(this.FileName(name), texture).
		/// </summary>
		/// <param name="name">The name.</param>
		/// <param name="texture">The texture.</param>
		/// <exception cref="Terraria.ModLoader.Exceptions.ModNameException">Texture already exist: " + name</exception>
		public void AddTexture(string name, Texture2D texture)
		{
			if (TextureExists(name))
				throw new Exception("Texture already exist: " + name);

			textures[name] = texture;
		}

		/// <summary>
		/// Shorthand for calling ModContent.GetSound(this.FileName(name)).
		/// </summary>
		/// <param name="name">The name.</param>
		/// <returns></returns>
		/// <exception cref="MissingResourceException"></exception>
		public SoundEffect GetSound(string name)
		{
			if (!sounds.TryGetValue(name, out var sound))
				throw new MissingResourceException(name);

			return sound;
		}

		/// <summary>
		/// Shorthand for calling ModLoader.SoundExists(this.FileName(name)).
		/// </summary>
		/// <param name="name">The name.</param>
		/// <returns></returns>
		public bool SoundExists(string name) => sounds.ContainsKey(name);

		/// <summary>
		/// Shorthand for calling ModContent.GetMusic(this.FileName(name)).
		/// </summary>
		/// <param name="name">The name.</param>
		/// <returns></returns>
		/// <exception cref="MissingResourceException"></exception>
		public Music GetMusic(string name)
		{
			if (!musics.TryGetValue(name, out var sound))
				throw new MissingResourceException(name);
			
			return sound.GetInstance();
		}

		/// <summary>
		/// Shorthand for calling ModLoader.MusicExists(this.FileName(name)).
		/// </summary>
		/// <param name="name">The name.</param>
		/// <returns></returns>
		public bool MusicExists(string name) => musics.ContainsKey(name);

		/// <summary>
		/// Gets a SpriteFont loaded from the specified path.
		/// </summary>
		/// <exception cref="MissingResourceException"></exception>
		public DynamicSpriteFont GetFont(string name)
		{
			if (!fonts.TryGetValue(name, out var font))
				throw new MissingResourceException(name);

			return font;
		}

		/// <summary>
		/// Used to check if a custom SpriteFont exists
		/// </summary>
		public bool FontExists(string name) => fonts.ContainsKey(name);

		/// <summary>
		/// Gets an Effect loaded from the specified path.
		/// </summary>
		/// <exception cref="MissingResourceException"></exception>
		public Effect GetEffect(string name)
		{
			if (!effects.TryGetValue(name, out var effect))
				throw new MissingResourceException(name);

			return effect;
		}

		/// <summary>
		/// Used to check if a custom Effect exists
		/// </summary>
		public bool EffectExists(string name) => effects.ContainsKey(name);

		/// <summary>
		/// Used for weak inter-mod communication. This allows you to interact with other mods without having to reference their types or namespaces, provided that they have implemented this method.
		/// </summary>
		public virtual object Call(params object[] args)
		{
			return null;
		}

		/// <summary>
		/// Creates a ModPacket object that you can write to and then send between servers and clients.
		/// </summary>
		/// <param name="capacity">The capacity.</param>
		/// <returns></returns>
		/// <exception cref="System.Exception">Cannot get packet for " + Name + " because it does not exist on the other side</exception>
		public ModPacket GetPacket(int capacity = 256)
		{
			if (netID < 0)
				throw new Exception("Cannot get packet for " + Name + " because it does not exist on the other side");

			var p = new ModPacket(MessageID.ModPacket, capacity + 5);
			p.Write(netID);
			return p;
		}
	}
}
