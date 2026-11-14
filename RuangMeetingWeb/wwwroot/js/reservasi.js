// Reservation form: show room capacity, duration and early warnings.
// The API is the authority; this only gives instant feedback.
(function () {
    const sel = document.getElementById('ruanganId');
    if (!sel) return;
    const kapasitas = document.getElementById('kapasitas');
    const peserta = document.getElementById('jumlahPeserta');
    const mulai = document.getElementById('jamMulai');
    const selesai = document.getElementById('jamSelesai');
    const hintPeserta = document.getElementById('hintPeserta');
    const hintDurasi = document.getElementById('hintDurasi');

    const menit = v => { if (!v) return null; const [h, m] = v.split(':').map(Number); return h * 60 + m; };

    function update() {
        const opt = sel.selectedOptions[0];
        const kap = opt ? Number(opt.dataset.kapasitas) : NaN;
        kapasitas.value = isNaN(kap) ? '' : kap;

        const p = Number(peserta.value);
        hintPeserta.textContent = !isNaN(kap) && p > kap ? `Melebihi kapasitas (${kap})` : '';

        const a = menit(mulai.value), b = menit(selesai.value);
        if (a == null || b == null) { hintDurasi.textContent = ''; return; }
        const d = b - a;
        hintDurasi.className = 'note';
        if (d <= 0) { hintDurasi.textContent = 'Jam mulai harus lebih awal dari jam selesai'; hintDurasi.classList.add('hint-warn'); }
        else if (d < 30) { hintDurasi.textContent = 'Durasi minimal 30 menit'; hintDurasi.classList.add('hint-warn'); }
        else if (d > 480) { hintDurasi.textContent = 'Durasi maksimal 8 jam'; hintDurasi.classList.add('hint-warn'); }
        else hintDurasi.textContent = `Durasi ${Math.floor(d / 60)} jam ${d % 60 ? (d % 60) + ' menit' : ''}`.trim();
    }

    [sel, peserta, mulai, selesai].forEach(el => { el.addEventListener('change', update); el.addEventListener('input', update); });
    update();
})();
