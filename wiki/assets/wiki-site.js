(function () {
  const body = document.body;
  const currentPage = body.dataset.page;
  const storageKey = "xecli-wiki-groups";

  function loadGroupState() {
    try {
      return JSON.parse(localStorage.getItem(storageKey) || "{}");
    } catch {
      return {};
    }
  }

  function saveGroupState(state) {
    localStorage.setItem(storageKey, JSON.stringify(state));
  }

  function wireGroups() {
    const saved = loadGroupState();
    document.querySelectorAll(".wiki-nav__group").forEach((group) => {
      const id = group.dataset.group;
      const containsCurrent = group.querySelector(`a[data-page="${currentPage}"]`);
      if (Object.prototype.hasOwnProperty.call(saved, id)) {
        group.open = !!saved[id];
      } else if (containsCurrent) {
        group.open = true;
      }

      group.addEventListener("toggle", () => {
        const state = loadGroupState();
        state[id] = group.open;
        saveGroupState(state);
      });
    });
  }

  function wireFilter() {
    const input = document.getElementById("wiki-filter");
    if (!input) return;
    input.addEventListener("input", () => {
      const query = input.value.trim().toLowerCase();
      document.querySelectorAll(".wiki-nav__group").forEach((group) => {
        let visibleCount = 0;
        group.querySelectorAll(".wiki-nav__link").forEach((link) => {
          const visible = !query || link.textContent.toLowerCase().includes(query);
          link.style.display = visible ? "" : "none";
          if (visible) visibleCount++;
        });
        group.style.display = visibleCount > 0 ? "" : "none";
        if (query && visibleCount > 0) {
          group.open = true;
        }
      });
    });
  }

  function slugify(text) {
    return text
      .toLowerCase()
      .trim()
      .replace(/[^\w\s-]/g, "")
      .replace(/\s+/g, "-");
  }

  function buildToc() {
    const content = document.getElementById("wiki-content");
    const toc = document.getElementById("wiki-toc");
    if (!content || !toc) return;

    const headings = [...content.querySelectorAll("h2, h3")];
    if (headings.length === 0) {
      toc.innerHTML = "<span>No section headings on this page.</span>";
      return;
    }

    toc.innerHTML = "";
    headings.forEach((heading) => {
      if (!heading.id) {
        heading.id = slugify(heading.textContent || "section");
      }
      const link = document.createElement("a");
      link.href = `#${heading.id}`;
      link.textContent = heading.textContent || "";
      link.dataset.level = heading.tagName.replace("H", "");
      toc.appendChild(link);
    });
  }

  wireGroups();
  wireFilter();
  buildToc();
})();
