"""Development/test entry point; releases use CaptchaOcr.py --portal."""
from CaptchaOcr import Store, make_server, portal_main

if __name__ == "__main__":
    portal_main()
