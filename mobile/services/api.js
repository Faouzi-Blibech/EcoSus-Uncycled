/**
 * api.js — Uncycled API Client
 *
 * Handles the multipart/form-data POST to the FastAPI backend.
 * The key design decision: we build the FormData payload from the
 * camera's raw file URI, never touching the gallery. This enforces
 * the "live capture only" anti-cheat rule on the client side.
 */
import axios from "axios";
import { Platform } from "react-native";

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
  timeout: 30000, // CLIP inference can take a few seconds on CPU
});

/**
 * Sends a freshly captured photo to the backend for verification.
 *
 * @param {string} photoUri  – The local file URI returned by expo-camera
 *                              (e.g. "file:///data/user/0/.../photo.jpg")
 * @returns {Promise<Object>} – The parsed JSON response:
 *   {
 *     verified: boolean,
 *     detected_item?: string,
 *     confidence_score?: number,
 *     message: string
 *   }
 */
export async function verifyDailyPhoto(photoUri) {
  // Build the multipart form exactly as FastAPI expects:
  //   - field name: "file"  (matches `file: UploadFile = File(...)`)
  //   - MIME type:  image/jpeg (expo-camera default)
  const formData = new FormData();
  formData.append("file", {
    uri: photoUri,
    name: "eco-capture.jpg",
    type: "image/jpeg",
  });

  const response = await apiClient.post("/verify-daily-photo", formData, {
    headers: {
      "Content-Type": "multipart/form-data",
    },
  });

  return response.data;
}

export default apiClient;
