package com.fimagedev.fimagestack.ui.components

import android.graphics.Bitmap
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.AutoAwesome
import androidx.compose.material.icons.filled.Check
import androidx.compose.material.icons.filled.Layers
import androidx.compose.material3.*
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.fimagedev.fimagestack.ui.theme.*

data class MosaicTile(
    val id: Int,
    val label: String, // "Top-Left", "Top-Right", "Bottom-Left", "Bottom-Right"
    val bitmap: Bitmap? = null
)

@Composable
fun SubPartMosaicHud(
    tiles: List<MosaicTile>,
    activeTileIndex: Int,
    onTileSelected: (Int) -> Unit,
    onStitchAllTiles: () -> Unit,
    modifier: Modifier = Modifier
) {
    val completedCount = tiles.count { it.bitmap != null }

    Box(
        modifier = modifier
            .clip(RoundedCornerShape(16.dp))
            .background(BgPanel.copy(alpha = 0.90f))
            .border(1.dp, BorderDefault, RoundedCornerShape(16.dp))
            .padding(10.dp)
    ) {
        Column(horizontalAlignment = Alignment.CenterHorizontally) {
            Row(
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.SpaceBetween,
                modifier = Modifier.width(140.dp)
            ) {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Icon(Icons.Default.Layers, contentDescription = "Mosaic", tint = PrimaryCyan, modifier = Modifier.size(16.dp))
                    Spacer(modifier = Modifier.width(4.dp))
                    Text("SUB-PART MOSAIC", fontSize = 10.sp, fontWeight = FontWeight.Bold, color = PrimaryCyan)
                }
                Text("$completedCount/${tiles.size}", fontSize = 10.sp, fontWeight = FontWeight.Bold, color = PrimaryNeonGreen)
            }

            Spacer(modifier = Modifier.height(8.dp))

            // 2x2 Mini Map Grid
            Column(
                verticalArrangement = Arrangement.spacedBy(4.dp),
                horizontalAlignment = Alignment.CenterHorizontally
            ) {
                // Row 1 (Top-Left, Top-Right)
                Row(horizontalArrangement = Arrangement.spacedBy(4.dp)) {
                    TileCell(tile = tiles.getOrNull(0), isSelected = activeTileIndex == 0, onClick = { onTileSelected(0) })
                    TileCell(tile = tiles.getOrNull(1), isSelected = activeTileIndex == 1, onClick = { onTileSelected(1) })
                }
                // Row 2 (Bottom-Left, Bottom-Right)
                Row(horizontalArrangement = Arrangement.spacedBy(4.dp)) {
                    TileCell(tile = tiles.getOrNull(2), isSelected = activeTileIndex == 2, onClick = { onTileSelected(2) })
                    TileCell(tile = tiles.getOrNull(3), isSelected = activeTileIndex == 3, onClick = { onTileSelected(3) })
                }
            }

            if (completedCount >= 2) {
                Spacer(modifier = Modifier.height(8.dp))
                Button(
                    onClick = onStitchAllTiles,
                    colors = ButtonDefaults.buttonColors(containerColor = PrimaryNeonGreen, contentColor = BgDark),
                    shape = RoundedCornerShape(8.dp),
                    contentPadding = PaddingValues(horizontal = 8.dp, vertical = 4.dp),
                    modifier = Modifier.height(30.dp)
                ) {
                    Icon(Icons.Default.AutoAwesome, contentDescription = null, modifier = Modifier.size(14.dp))
                    Spacer(modifier = Modifier.width(4.dp))
                    Text("STITCH ($completedCount)", fontSize = 10.sp, fontWeight = FontWeight.Black)
                }
            }
        }
    }
}

@Composable
private fun TileCell(
    tile: MosaicTile?,
    isSelected: Boolean,
    onClick: () -> Unit
) {
    val hasBitmap = tile?.bitmap != null

    Box(
        modifier = Modifier
            .size(44.dp)
            .clip(RoundedCornerShape(6.dp))
            .background(if (isSelected) PrimaryNeonGreen.copy(alpha = 0.2f) else BgCard)
            .border(
                width = if (isSelected) 2.dp else 1.dp,
                color = if (isSelected) PrimaryNeonGreen else if (hasBitmap) PrimaryCyan else BorderDefault,
                shape = RoundedCornerShape(6.dp)
            )
            .clickable { onClick() },
        contentAlignment = Alignment.Center
    ) {
        if (tile?.bitmap != null) {
            Image(
                bitmap = tile.bitmap.asImageBitmap(),
                contentDescription = tile.label,
                contentScale = ContentScale.Crop,
                modifier = Modifier.fillMaxSize()
            )
            Box(
                modifier = Modifier
                    .size(14.dp)
                    .clip(CircleShape)
                    .background(PrimaryNeonGreen)
                    .align(Alignment.BottomEnd),
                contentAlignment = Alignment.Center
            ) {
                Icon(Icons.Default.Check, contentDescription = null, tint = BgDark, modifier = Modifier.size(10.dp))
            }
        } else {
            Text(
                text = tile?.label?.take(2)?.uppercase() ?: "--",
                fontSize = 9.sp,
                fontWeight = FontWeight.Bold,
                color = if (isSelected) PrimaryNeonGreen else TextSecondary
            )
        }
    }
}
