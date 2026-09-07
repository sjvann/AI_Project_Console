export default {
  iconLinks: [
    {
      icon: "github",
      href: "https://github.com/sjvann/AI_Project_Console",
      title: "GitHub",
    },
  ],
  mermaid: {
    theme: "neutral",
  },
  start: () => {
    const input = document.getElementById("search-query");
    if (input && !input.dataset.zhPlaceholder) {
      input.placeholder = "搜尋文件";
      input.dataset.zhPlaceholder = "1";
    }
  },
};
    const input = document.getElementById("search-query");
    if (input && !input.dataset.zhPlaceholder) {
      input.placeholder = "搜尋文件";
      input.dataset.zhPlaceholder = "1";
    }
  },
};
