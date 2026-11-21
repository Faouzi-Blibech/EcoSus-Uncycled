/**
 * App.js — Uncycled: Eco-Streak Photo Verification
 *
 * Main app shell. Manages three states:
 *   1. CAMERA   → Live camera viewfinder (CameraScreen)
 *   2. LOADING  → Uploading + AI verification in progress
 *   3. RESULT   → Show verification result + skin unlocks (ResultScreen)
 *
 * Flow: Camera capture → multipart POST → parse response → show result
 * No gallery access at any point — enforces live capture anti-cheat.
 */
import React, { useState } from "react";
import {
  View,
  Text,
  StyleSheet,
  ActivityIndicator,
  SafeAreaView,
  StatusBar,
} from "react-native";
import CameraScreen from "./components/CameraScreen";
import ResultScreen from "./components/ResultScreen";
import { verifyDailyPhoto } from "./services/api";
import {
  recordVerifiedStreak,
  getStreakState,
} from "./services/streakManager";

// ─── App States ───────────────────────────────────────────────
const SCREEN = {
  CAMERA: "CAMERA",
  LOADING: "LOADING",
  RESULT: "RESULT",
};

export default function App() {
  const [screen, setScreen] = useState(SCREEN.CAMERA);
  const [photoUri, setPhotoUri] = useState(null);
  const [apiResult, setApiResult] = useState(null);
  const [streakInfo, setStreakInfo] = useState(null);
  const [streakState, setStreakState] = useState(null);
  const [errorMessage, setErrorMessage] = useState(null);

  /**
   * Called by CameraScreen when a photo is captured.
   * Immediately sends it to the backend — no preview/confirm step
   * to keep the UX fast during the hackathon demo.
   */
  const handlePhotoCaptured = async (uri) => {
    setPhotoUri(uri);
    setScreen(SCREEN.LOADING);
    setErrorMessage(null);

    try {
      // ── 1. Send to FastAPI backend ────────────────────────
      const result = await verifyDailyPhoto(uri);
      setApiResult(result);

      // ── 2. Process streak & skin unlocks (if verified) ────
      if (result.verified) {
        const info = recordVerifiedStreak(result.co2_saved_grams || 0);
        setStreakInfo(info);
      } else {
        setStreakInfo(null);
      }
      setStreakState(getStreakState());

      // ── 3. Show result ────────────────────────────────────
      setScreen(SCREEN.RESULT);
    } catch (error) {
      console.error("Verification failed:", error);

      // Build a user-friendly error from the axios error
      let message = "Something went wrong. Please try again.";
      if (error.response) {
        // Server responded with an error (e.g., 400 invalid file type)
        message =
          error.response.data?.detail ||
          error.response.data?.message ||
          `Server error (${error.response.status})`;
      } else if (error.code === "ECONNABORTED") {
        message = "Request timed out. The AI model is still warming up — try again in a moment.";
      } else if (error.message?.includes("Network Error")) {
        message =
          "Can't reach the server. Make sure your FastAPI backend is running and your phone is on the same WiFi network.";
      }

      setApiResult({
        verified: false,
        message: message,
      });
      setStreakInfo(null);
      setStreakState(getStreakState());
      setScreen(SCREEN.RESULT);
    }
  };

  /**
   * Reset to camera for another attempt.
   */
  const handleScanAgain = () => {
    setPhotoUri(null);
    setApiResult(null);
    setStreakInfo(null);
    setErrorMessage(null);
    setScreen(SCREEN.CAMERA);
  };

  // ─── Render ─────────────────────────────────────────────────
  switch (screen) {
    case SCREEN.CAMERA:
      return <CameraScreen onPhotoCaptured={handlePhotoCaptured} />;

    case SCREEN.LOADING:
      return (
        <SafeAreaView style={styles.loadingContainer}>
          <StatusBar barStyle="light-content" />
          <ActivityIndicator size="large" color="#2ECC71" />
          <Text style={styles.loadingTitle}>Verifying...</Text>
          <Text style={styles.loadingSubtitle}>
            🤖 AI is analyzing your recycling item
          </Text>
          <Text style={styles.loadingDetail}>
            Running YOLOE detection + anti-cheat check
          </Text>
        </SafeAreaView>
      );

    case SCREEN.RESULT:
      return (
        <ResultScreen
          photoUri={photoUri}
          result={apiResult}
          streakInfo={streakInfo}
          streakState={streakState}
          onScanAgain={handleScanAgain}
        />
      );

    default:
      return <CameraScreen onPhotoCaptured={handlePhotoCaptured} />;
  }
}

// ─── Styles ───────────────────────────────────────────────────
const styles = StyleSheet.create({
  loadingContainer: {
    flex: 1,
    justifyContent: "center",
    alignItems: "center",
    backgroundColor: "#1a1a2e",
  },
  loadingTitle: {
    color: "#fff",
    fontSize: 24,
    fontWeight: "bold",
    marginTop: 24,
  },
  loadingSubtitle: {
    color: "#aaa",
    fontSize: 16,
    marginTop: 8,
  },
  loadingDetail: {
    color: "#555",
    fontSize: 12,
    marginTop: 16,
  },
});
