"""Converts the chosen clips of the Clutch Assets "200 Free RPG Sounds" pack to content/sounds/<cue>.ogg (COS-101).

The pack lives outside the repo (default D:/dev/asset-sources/150+ Free RPG Sounds/150+ Free RPG Sounds).
Each clip is trimmed of leading and trailing silence, mixed to mono (FMOD 3D needs mono) and written as
44.1 kHz Ogg Vorbis. Credits: CREDITS.md at the repo root. Usage: python tools/sounds/build_sounds.py [pack_dir]
"""
import glob
import os
import subprocess
import sys

PACK = sys.argv[1] if len(sys.argv) > 1 else r"D:/dev/asset-sources/150+ Free RPG Sounds/150+ Free RPG Sounds"
OUT = os.path.join(os.path.dirname(__file__), "..", "..", "content", "sounds")

# cue id -> (file name prefix in the pack, max seconds or None)
CLIPS = {
    "level_up": ("Level_up_–_Magical_a_#1", None),
    "drop_low": ("Coin_pickup_–_Single_#2", 1.5),
    "drop_mid": ("Rune_activate_–_Anci_#2", None),
    "drop_high": ("Blessing_applied_–_L_#1", None),
    "drop_top": ("Achievement_unlock_–_#1", None),
    "pickup": ("Item_pickup_–_Soft_c_#1", None),
    "perk_bought": ("Quest_accepted_–_Pos_#1", None),
    "craft": ("Equip_weapon_–_Weapo_#1", None),
    "ui_click": ("Button_click_–_Clean_#2", None),
    "ui_open": ("Inventory_open_–_UI__#1", None),
    "cast_fire": ("Fire_spell_cast_–_Ma_#1", None),
    "impact_fire": ("Fire_spell_impact_–__#1", None),
    "cast_ice": ("Ice_spell_cast_–_Col_#1", 2.5),
    "impact_ice": ("Ice_impact_–_Ice_sha_#1", None),
    "cast_lightning": ("Lightning_cast_–_Ele_#1", None),
    "cast_projectile": ("Magic_projectile_lau_#1", None),
    "cast_buff": ("Buff_spell_–_Magical_#1", None),
    "cast_debuff": ("Debuff_spell_–_Dark__#1", None),
    "cast_warp": ("Rune_activate_–_Anci_#4", None),
    "cast_physical": ("Heavy_weapon_whoosh,_#3", None),
    "impact_metal": ("Shield_block_–_Metal_#1", None),
    # weapon types (COS-93)
    "swing_blade": ("Sword_swing_–_Fast_m_#4", None),
    "swing_heavy": ("Heavy_weapon_whoosh,_#4", None),
    "hit_blade": ("Sword_hit_flesh_–_Me_#1", None),
    "hit_heavy": ("Axe_hit_armor_–_Deep_#1", None),
    "bow_shot": ("Bow_and_arrow_shot_–_#3", None),
    "trap_set": ("Lever_pull_–_Mechani_#1", None),
    "loop_wind": ("Forest_ambience_–_Wi_#1", None),
    "loop_fire": ("Torch_fire_–_Small_f_#1", None),
}

LOOPS = {"loop_wind", "loop_fire"}


def main():
    os.makedirs(OUT, exist_ok=True)
    for cue, (prefix, max_s) in CLIPS.items():
        found = sorted(glob.glob(os.path.join(PACK, glob.escape(prefix) + "*.mp3")))
        if not found:
            sys.exit(f"missing clip for {cue}: {prefix}")
        # Loops keep their full length so the seam stays where the recording has it.
        trim = [] if cue in LOOPS else ["-af", "silenceremove=start_periods=1:start_threshold=-55dB,areverse,silenceremove=start_periods=1:start_threshold=-55dB,areverse"]
        cut = ["-t", str(max_s)] if max_s else []
        dst = os.path.join(OUT, cue + ".ogg")
        subprocess.run(["ffmpeg", "-v", "error", "-y", "-i", found[0], *trim, *cut, "-ac", "1", "-ar", "44100",
                        "-c:a", "libvorbis", "-q:a", "3", dst], check=True)
        print(f"{cue:16} <- {os.path.basename(found[0])} ({os.path.getsize(dst) // 1024} KB)")


if __name__ == "__main__":
    main()
