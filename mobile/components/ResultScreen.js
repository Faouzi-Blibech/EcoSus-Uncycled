/**
 * ResultScreen.js — Verification Result Display
 *
 * Shows the user whether their recycling photo was verified or rejected.
 * On success: triggers the cosmetic skin unlock overlay, shows streak info,
 * and displays the CO₂ environmental impact of this scan.
 * On failure: displays the specific rejection reason (AI miss, anti-cheat, or EXIF).
 */
import React from "react";
import {
  View,
  Text,
  TouchableOpacity,
  StyleSheet,
  ScrollView,
  SafeAreaView,
  StatusBar,
} from "react-native";
import SkinUnlockOverlay from "./SkinUnlockOverlay";
import DetectionOverlay from "./DetectionOverlay";

export default function ResultScreen({
  photoUri,
  result,          // API response, see services/api.js
  onScanAgain,     // Reset back to camera
}) {
  const isVerified = result?.verified === true;
  const isAntiCheat = result?.reason === "duplicate";
  const isExifFail = result?.reason === "exif";
  const player = result?.player; // server-owned streak, totals and skins

  return (
    <SafeAreaView
      style={[
        styles.container,
        { backgroundColor: isVerified ? "#0d2818" : "#2d0a0a" },
      ]}
    >
      <StatusBar barStyle="light-content" />
      <ScrollView
        contentContainerStyle={styles.scrollContent}
        showsVerticalScrollIndicator={false}
      >
        {/* ── Result Header ──────────────────────────────────── */}
        <View style={styles.header}>
          <Text style={styles.resultEmoji}>
            {isVerified ? "✅" : isAntiCheat ? "🚫" : isExifFail ? "📷" : "❌"}
          </Text>
          <Text
            style={[
              styles.resultTitle,
              { color: isVerified ? "#2ECC71" : "#E74C3C" },
            ]}
          >
            {isVerified
              ? "Eco-Streak Validated!"
              : isAntiCheat
              ? "Duplicate Detected"
              : isExifFail
              ? "Live Photo Required"
              : result?.reason === "not_waste"
              ? "Not Waste"
              : result?.reason === "low_confidence"
              ? "Not Sure — Try Again"
              : "Not Recognized"}
          </Text>
          <Text style={styles.resultMessage}>{result?.message}</Text>
        </View>

        {/* ── Photo with the detected item's outline ────────── */}
        <DetectionOverlay photoUri={photoUri} result={result} />

        {/* ── CO₂ Impact Card (on success) ───────────────────── */}
        {isVerified && (
          <View style={styles.co2Card}>
            <Text style={styles.co2Title}>🌍 Environmental Impact</Text>
            <Text style={styles.co2Number}>-{result.co2_saved_grams}g</Text>
            <Text style={styles.co2Label}>CO₂ saved with this scan</Text>
            {result.co2_equivalence && (
              <Text style={styles.co2Equivalence}>{result.co2_equivalence}</Text>
            )}
            {result.co2_note && (
              <Text style={styles.co2Note}>{result.co2_note}</Text>
            )}
            {result.bin && (
              <Text style={styles.binText}>
                🗑️ {result.bin}
                {result.disposal_tip ? `\n💡 ${result.disposal_tip}` : ""}
              </Text>
            )}
            {player?.total_co2_grams > 0 && (
              <View style={styles.co2Lifetime}>
                <Text style={styles.co2LifetimeLabel}>Your lifetime impact:</Text>
                <Text style={styles.co2LifetimeValue}>
                  {player.total_co2_grams >= 1000
                    ? `${player.total_co2_kg} kg`
                    : `${player.total_co2_grams}g`}{" "}
                  CO₂ saved across {player.total_items} item
                  {player.total_items !== 1 ? "s" : ""}
                </Text>
              </View>
            )}
          </View>
        )}

        {/* ── Detection Details ──────────────────────────────── */}
        {(result?.detected_item || result?.detected_class) && (
          <View style={styles.detailsCard}>
            <Text style={styles.detailsTitle}>AI Detection</Text>
            <View style={styles.detailRow}>
              <Text style={styles.detailLabel}>Item:</Text>
              <Text style={styles.detailValue}>
                {result.detected_item || result.detected_class}
              </Text>
            </View>
            {result.confidence_score != null && (
              <View style={styles.detailRow}>
                <Text style={styles.detailLabel}>Confidence:</Text>
                <View style={styles.confidenceBar}>
                  <View
                    style={[
                      styles.confidenceFill,
                      {
                        width: `${Math.round(result.confidence_score * 100)}%`,
                        backgroundColor:
                          result.confidence_score > 0.8
                            ? "#2ECC71"
                            : result.confidence_score > 0.6
                            ? "#F39C12"
                            : "#E74C3C",
                      },
                    ]}
                  />
                </View>
                <Text style={styles.confidenceText}>
                  {Math.round(result.confidence_score * 100)}%
                </Text>
              </View>
            )}
          </View>
        )}

        {/* ── Streak Info (on success) ───────────────────────── */}
        {isVerified && player && (
          <View style={styles.streakCard}>
            <Text style={styles.streakTitle}>🔥 Eco-Streak</Text>
            <Text style={styles.streakNumber}>{player.streak}</Text>
            <Text style={styles.streakDays}>
              day{player.streak !== 1 ? "s" : ""} in a row
            </Text>
            {player.next_skin && (
              <Text style={styles.nextSkinText}>
                Next unlock: {player.next_skin.emoji}{" "}
                {player.next_skin.name} at {player.next_skin.required_streak}-day streak
              </Text>
            )}
          </View>
        )}

        {/* ── Anti-Cheat Explanation ──────────────────────────── */}
        {isAntiCheat && (
          <View style={styles.antiCheatCard}>
            <Text style={styles.antiCheatTitle}>⚠️ What happened?</Text>
            <Text style={styles.antiCheatText}>
              This item matches one you already scanned (same photo, or the same
              item from another angle today). Each physical item can only count
              once — go find a new one to recycle! ♻️
            </Text>
          </View>
        )}

        {/* ── EXIF / Live-Capture Explanation ─────────────────── */}
        {isExifFail && (
          <View style={styles.antiCheatCard}>
            <Text style={styles.antiCheatTitle}>📷 Live Photo Required</Text>
            <Text style={styles.antiCheatText}>
              We verify that your photo was taken just now using camera metadata.
              Please use the in-app camera to take a fresh photo of your recycling
              item — screenshots and downloaded images won't work!
            </Text>
          </View>
        )}

        {/* ── Scan Again Button ──────────────────────────────── */}
        <TouchableOpacity
          style={[
            styles.scanButton,
            { backgroundColor: isVerified ? "#2ECC71" : "#3498DB" },
          ]}
          onPress={onScanAgain}
          activeOpacity={0.7}
        >
          <Text style={styles.scanButtonText}>
            {isVerified ? "🎉 Done" : "📸 Try Again"}
          </Text>
        </TouchableOpacity>
      </ScrollView>

      {/* ── Skin Unlock Celebration Overlay ───────────────────── */}
      {player?.new_unlocks?.length > 0 && (
        <SkinUnlockOverlay skins={player.new_unlocks} />
      )}
    </SafeAreaView>
  );
}

// ─── Styles ───────────────────────────────────────────────────
const styles = StyleSheet.create({
  container: {
    flex: 1,
  },
  scrollContent: {
    padding: 24,
    alignItems: "center",
  },
  header: {
    alignItems: "center",
    marginBottom: 20,
  },
  resultEmoji: {
    fontSize: 64,
    marginBottom: 12,
  },
  resultTitle: {
    fontSize: 24,
    fontWeight: "bold",
    marginBottom: 8,
  },
  resultMessage: {
    color: "#ccc",
    fontSize: 16,
    textAlign: "center",
  },
  // CO₂ Impact Card
  co2Card: {
    backgroundColor: "rgba(46,204,113,0.12)",
    borderRadius: 16,
    padding: 20,
    width: "100%",
    alignItems: "center",
    marginBottom: 16,
    borderWidth: 1,
    borderColor: "rgba(46,204,113,0.25)",
  },
  co2Title: {
    color: "#2ECC71",
    fontSize: 16,
    fontWeight: "bold",
    marginBottom: 8,
  },
  co2Number: {
    color: "#2ECC71",
    fontSize: 42,
    fontWeight: "bold",
  },
  co2Label: {
    color: "#aaa",
    fontSize: 13,
    marginBottom: 6,
  },
  co2Equivalence: {
    color: "#7DCEA0",
    fontSize: 14,
    fontStyle: "italic",
    textAlign: "center",
    marginBottom: 12,
  },
  co2Note: {
    color: "#F5B041",
    fontSize: 13,
    textAlign: "center",
    marginBottom: 10,
  },
  binText: {
    color: "#ddd",
    fontSize: 13,
    textAlign: "center",
    lineHeight: 20,
    marginBottom: 12,
  },
  co2Lifetime: {
    borderTopWidth: 1,
    borderTopColor: "rgba(255,255,255,0.1)",
    paddingTop: 12,
    width: "100%",
    alignItems: "center",
  },
  co2LifetimeLabel: {
    color: "#888",
    fontSize: 12,
    marginBottom: 4,
  },
  co2LifetimeValue: {
    color: "#fff",
    fontSize: 14,
    fontWeight: "600",
    textAlign: "center",
  },
  // Detection details
  detailsCard: {
    backgroundColor: "rgba(255,255,255,0.08)",
    borderRadius: 16,
    padding: 20,
    width: "100%",
    marginBottom: 16,
  },
  detailsTitle: {
    color: "#fff",
    fontSize: 16,
    fontWeight: "bold",
    marginBottom: 12,
  },
  detailRow: {
    flexDirection: "row",
    alignItems: "center",
    marginBottom: 8,
  },
  detailLabel: {
    color: "#aaa",
    fontSize: 14,
    width: 90,
  },
  detailValue: {
    color: "#fff",
    fontSize: 14,
    fontWeight: "600",
    textTransform: "capitalize",
    flex: 1,
  },
  confidenceBar: {
    flex: 1,
    height: 8,
    backgroundColor: "rgba(255,255,255,0.1)",
    borderRadius: 4,
    overflow: "hidden",
    marginRight: 8,
  },
  confidenceFill: {
    height: "100%",
    borderRadius: 4,
  },
  confidenceText: {
    color: "#fff",
    fontSize: 14,
    fontWeight: "bold",
    width: 42,
    textAlign: "right",
  },
  // Streak card
  streakCard: {
    backgroundColor: "rgba(46,204,113,0.15)",
    borderRadius: 16,
    padding: 20,
    width: "100%",
    alignItems: "center",
    marginBottom: 16,
    borderWidth: 1,
    borderColor: "rgba(46,204,113,0.3)",
  },
  streakTitle: {
    color: "#F39C12",
    fontSize: 18,
    fontWeight: "bold",
    marginBottom: 4,
  },
  streakNumber: {
    color: "#fff",
    fontSize: 48,
    fontWeight: "bold",
  },
  streakDays: {
    color: "#aaa",
    fontSize: 14,
    marginBottom: 8,
  },
  nextSkinText: {
    color: "#7DCEA0",
    fontSize: 13,
    textAlign: "center",
  },
  // Anti-cheat / EXIF card
  antiCheatCard: {
    backgroundColor: "rgba(231,76,60,0.15)",
    borderRadius: 16,
    padding: 20,
    width: "100%",
    marginBottom: 16,
    borderWidth: 1,
    borderColor: "rgba(231,76,60,0.3)",
  },
  antiCheatTitle: {
    color: "#E74C3C",
    fontSize: 16,
    fontWeight: "bold",
    marginBottom: 8,
  },
  antiCheatText: {
    color: "#ddd",
    fontSize: 14,
    lineHeight: 22,
  },
  // Scan button
  scanButton: {
    paddingHorizontal: 48,
    paddingVertical: 16,
    borderRadius: 30,
    marginTop: 8,
    marginBottom: 32,
  },
  scanButtonText: {
    color: "#fff",
    fontSize: 18,
    fontWeight: "bold",
  },
});
