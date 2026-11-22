/**
 * playerId.js — Stable anonymous player id.
 *
 * A random UUID created on first launch and kept in AsyncStorage. The server
 * keys streaks, CO₂ totals and unlocked skins on it, so the phone never
 * decides any of those itself.
 */
import AsyncStorage from "@react-native-async-storage/async-storage";
import * as Crypto from "expo-crypto";

const KEY = "uncycled_player_id";
let cached = null;

export async function getPlayerId() {
  if (cached) return cached;
  let id = await AsyncStorage.getItem(KEY);
  if (!id) {
    id = Crypto.randomUUID();
    await AsyncStorage.setItem(KEY, id);
  }
  cached = id;
  return id;
}
