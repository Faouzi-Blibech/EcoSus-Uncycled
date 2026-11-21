/**
 * api.js — Uncycled API Client
 *
 * Sends the camera photo to the verifier and reads the player's profile.
 * The server owns the streak, CO₂ totals and skins; this file only carries
 * the player id and the phone's timezone (for the EXIF freshness check).
 */
import axios from "axios";
import { Platform } from "react-native";
import { getPlayerId } from "./playerId";

// ─── Configuration ─────────────────────────────────────────────
// For Expo Go on a physical device, use your machine's LAN IP.
// For an Android emulator, 10.0.2.2 maps to the host's localhost.
// For an iOS simulator, plain localhost works.
const API_BASE_URL = __DEV__
  ? Platform.select({
      android: "http://10.0.2.2:8000",
      ios: "http://localhost:8000",
      default: "http://localhost:8000",
    })
  : "https://your-production-url.com"; // Replace for deployment

const apiClient = axios.create({
  baseURL: API_BASE_URL,
  timeout: 30000, // first request after a server start can be slow while models warm up
});

async function headers() {
  return {
    "X-Player-Id": await getPlayerId(),
    // Same sign as JS: UTC+1 -> -60. The server turns EXIF local time into UTC with it.
    "X-Tz-Offset-Minutes": String(new Date().getTimezoneOffset()),
  };
}

/**
 * Sends a freshly captured photo for verification.
 *
 * @param {string} photoUri – local file URI from expo-camera
 * @returns {Promise<Object>} – server response. Always has
 *   { verified, reason, message }, plus when an item was seen:
 *   { detected_class, confidence_score, bbox, image_size: [w, h],
 *     contour: [[x, y], ...] (0..1 of the upright photo) }
 *   and on success: { stream, detected_item, kind, bin, color,
 *     co2_saved_grams, co2_note, co2_equivalence, disposal_tip, player }
 */
export async function verifyDailyPhoto(photoUri) {
  const formData = new FormData();
  formData.append("file", {
    uri: photoUri,
    name: "eco-capture.jpg",
    type: "image/jpeg",
  });

  const response = await apiClient.post("/verify-daily-photo", formData, {
    headers: { "Content-Type": "multipart/form-data", ...(await headers()) },
  });
  return response.data;
}

/**
 * The player's streak, totals and unlocked skins, straight from the server.
 */
export async function getProfile() {
  const id = await getPlayerId();
  const response = await apiClient.get(`/players/${id}`);
  return response.data;
}

export default apiClient;
